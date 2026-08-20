#include "server.h"
#include "version.h"
#include <QDateTime>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QLoggingCategory>
#include <QTimer>
#include <QWebSocket>
#include <QWebSocketServer>

static const qint64 STALE_MS        = 15'000;
static const int    CLEANUP_MS      = 5'000;
static const int    ROSTER_FLUSH_MS = 50;
static const qint64 SILENCE_GAP_MS  = 1'000;

static bool versionCompatible(const QString& clientVersion) {
    const auto sv = QString(AppVersion::String).split(QLatin1Char('.'));
    const auto cv = clientVersion.split(QLatin1Char('.'));
    if (sv.size() < 2 || cv.size() < 2) return false;
    return sv[0] == cv[0] && sv[1] == cv[1];
}

static QString toJson(const QJsonObject& obj) {
    return QString::fromUtf8(QJsonDocument(obj).toJson(QJsonDocument::Compact));
}

// ---------------------------------------------------------------------------

RelayServer::RelayServer(const Config& config, bool verbose, QObject* parent)
    : QObject(parent), m_config(config), m_verbose(verbose)
{}

RelayServer::~RelayServer() {
    if (m_server) m_server->close();
    qDeleteAll(m_sessions);
}

bool RelayServer::listen() {
    m_server = new QWebSocketServer(
        QStringLiteral("GW2ProximityChat"),
        QWebSocketServer::NonSecureMode,
        this);

    if (!m_server->listen(QHostAddress::Any, static_cast<quint16>(m_config.port))) {
        qCritical("Failed to listen on port %d: %s",
                  m_config.port, qPrintable(m_server->errorString()));
        return false;
    }

    connect(m_server, &QWebSocketServer::newConnection, this, [this]() {
        while (m_server->hasPendingConnections()) {
            QWebSocket* ws = m_server->nextPendingConnection();

            if (m_config.userLimit > 0 && m_connectionCount >= m_config.userLimit) {
                qInfo("Rejected connection from %s (server full %d/%d)",
                      qPrintable(ws->peerAddress().toString()),
                      m_connectionCount, m_config.userLimit);
                sendText(ws, toJson({{"Type", "server_full"}}));
                ws->close();
                ws->deleteLater();
                continue;
            }

            ++m_connectionCount;
            auto* session       = new Session();
            session->ws         = ws;
            session->lastSeenMs = QDateTime::currentMSecsSinceEpoch();
            m_sessions[ws]      = session;

            connect(ws, &QWebSocket::textMessageReceived,   this, [this, ws](const QString& msg)    { handleTextMessage(ws, msg); });
            connect(ws, &QWebSocket::binaryMessageReceived, this, [this, ws](const QByteArray& data) { handleBinaryMessage(ws, data); });
            connect(ws, &QWebSocket::disconnected,          this, [this, ws]()                       { handleDisconnect(ws); });

            sendText(ws, toJson({
                {"Type",          "hello"},
                {"ServerName",    m_config.serverName},
                {"ServerVersion", AppVersion::String},
            }));

            qInfo("Connection from %s (%d/%s)",
                  qPrintable(ws->peerAddress().toString()),
                  m_connectionCount,
                  m_config.userLimit == 0 ? "unlimited" : qPrintable(QString::number(m_config.userLimit)));
        }
    });

    m_cleanupTimer = new QTimer(this);
    connect(m_cleanupTimer, &QTimer::timeout, this, &RelayServer::cleanupStale);
    m_cleanupTimer->start(CLEANUP_MS);

    m_rosterTimer = new QTimer(this);
    connect(m_rosterTimer, &QTimer::timeout, this, &RelayServer::flushDirtyRosters);
    m_rosterTimer->start(ROSTER_FLUSH_MS);

    return true;
}

// ---------------------------------------------------------------------------

void RelayServer::handleTextMessage(QWebSocket* ws, const QString& msg) {
    Session* session = m_sessions.value(ws);
    if (!session) return;

    const QJsonObject obj = QJsonDocument::fromJson(msg.toUtf8()).object();
    if (obj.isEmpty()) return;

    const QString playerId = obj[QStringLiteral("PlayerId")].toString();
    if (playerId.isEmpty()) return; // ping or unrecognised — ignore

    const bool hadIdentity = session->hasIdentity();
    const QString prevGroup = hadIdentity ? session->groupKey() : QString();

    // First-identity checks: version then password.
    if (!hadIdentity) {
        const QString clientVer = obj[QStringLiteral("Version")].toString();
        if (!versionCompatible(clientVer)) {
            qInfo("Rejected %s: version mismatch (client '%s', server '%s')",
                  qPrintable(playerId), qPrintable(clientVer), AppVersion::String);
            sendText(ws, toJson({
                {"Type",          "version_mismatch"},
                {"ServerVersion", AppVersion::String},
                {"ClientVersion", clientVer},
            }));
            ws->close();
            return;
        }

        if (!m_config.password.isEmpty()) {
            const QString pw = obj[QStringLiteral("Password")].toString();
            if (pw != m_config.password) {
                qInfo("Rejected %s: wrong password", qPrintable(playerId));
                sendText(ws, toJson({
                    {"Type",   "auth_failed"},
                    {"Reason", "Invalid password"},
                }));
                ws->close();
                return;
            }
        }
    }

    session->playerId     = playerId;
    session->name         = obj[QStringLiteral("Name")].toString();
    session->mapId        = obj[QStringLiteral("MapId")].toInt();
    session->instanceKey  = obj[QStringLiteral("InstanceKey")].toString();
    session->lastSeenMs   = QDateTime::currentMSecsSinceEpoch();

    const QJsonArray pos = obj[QStringLiteral("Pos")].toArray();
    if (pos.size() == 3) {
        session->pos[0] = static_cast<float>(pos[0].toDouble());
        session->pos[1] = static_cast<float>(pos[1].toDouble());
        session->pos[2] = static_cast<float>(pos[2].toDouble());
    }
    const QJsonArray facing = obj[QStringLiteral("Facing")].toArray();
    if (facing.size() == 3) {
        session->facing[0] = static_cast<float>(facing[0].toDouble());
        session->facing[1] = static_cast<float>(facing[1].toDouble());
        session->facing[2] = static_cast<float>(facing[2].toDouble());
    }

    const QString newGroup = session->groupKey();

    if (!hadIdentity) {
        qInfo("Identified %s — map=%d instance=%s",
              qPrintable(session->label()), session->mapId, qPrintable(session->instanceKey));
        addToGroup(ws, newGroup);
    } else if (prevGroup != newGroup) {
        qInfo("%s changed group %s -> map=%d instance=%s",
              qPrintable(session->label()), qPrintable(prevGroup),
              session->mapId, qPrintable(session->instanceKey));
        removeFromGroup(ws, prevGroup);
        addToGroup(ws, newGroup);
        m_dirtyGroups.insert(prevGroup);
    }

    m_dirtyGroups.insert(newGroup);
}

void RelayServer::handleBinaryMessage(QWebSocket* ws, const QByteArray& data) {
    Session* session = m_sessions.value(ws);
    if (!session || !session->hasIdentity() || session->instanceKey.isEmpty()) {
        if (m_verbose)
            qDebug("Dropping audio from %s (no identity/instance yet)",
                   session ? qPrintable(session->label()) : "unknown");
        return;
    }

    const QByteArray idBytes = session->playerId.toUtf8();
    if (idBytes.size() > 255) return;

    const qint64 now = QDateTime::currentMSecsSinceEpoch();
    if (now - session->lastAudioAt > SILENCE_GAP_MS)
        qInfo("%s started talking", qPrintable(session->label()));
    session->lastAudioAt = now;
    session->audioFramesInWindow++;
    session->audioBytesInWindow += data.size();

    QByteArray frame;
    frame.reserve(1 + idBytes.size() + data.size());
    frame.append(static_cast<char>(idBytes.size()));
    frame.append(idBytes);
    frame.append(data);

    const auto recipients = groupMembers(session->groupKey(), ws);
    if (m_verbose)
        qDebug("Audio from %s: %d bytes -> %d recipient(s)",
               qPrintable(session->label()), data.size(), recipients.size());

    for (Session* r : recipients)
        sendBinary(r->ws, frame);
}

void RelayServer::handleDisconnect(QWebSocket* ws) {
    Session* session = m_sessions.take(ws);
    if (!session) return;

    --m_connectionCount;
    qInfo("Disconnected: %s", qPrintable(session->label()));

    if (session->hasIdentity()) {
        removeFromGroup(ws, session->groupKey());
        m_dirtyGroups.insert(session->groupKey());
    }

    delete session;
    ws->deleteLater();
}

// ---------------------------------------------------------------------------

void RelayServer::cleanupStale() {
    const qint64 now = QDateTime::currentMSecsSinceEpoch();

    if (m_verbose) {
        for (auto [ws, s] : m_sessions.asKeyValueRange()) {
            if (s->audioFramesInWindow > 0) {
                qDebug("Audio from %s: %d frames / %d bytes in last %ds",
                       qPrintable(s->label()),
                       s->audioFramesInWindow, s->audioBytesInWindow,
                       CLEANUP_MS / 1000);
            }
            s->audioFramesInWindow = 0;
            s->audioBytesInWindow  = 0;
        }
    } else {
        for (auto [ws, s] : m_sessions.asKeyValueRange()) {
            s->audioFramesInWindow = 0;
            s->audioBytesInWindow  = 0;
        }
    }

    QList<QWebSocket*> stale;
    for (auto [ws, s] : m_sessions.asKeyValueRange()) {
        if (s->hasIdentity() && now - s->lastSeenMs > STALE_MS)
            stale.append(ws);
    }
    for (QWebSocket* ws : stale) {
        if (Session* s = m_sessions.value(ws))
            qInfo("%s timed out (no state for %llds)", qPrintable(s->label()), STALE_MS / 1000);
        ws->close();
    }
}

void RelayServer::flushDirtyRosters() {
    for (const QString& key : qAsConst(m_dirtyGroups))
        broadcastRoster(key);
    m_dirtyGroups.clear();
}

void RelayServer::broadcastRoster(const QString& groupKey) {
    if (groupKey.isEmpty()) return;
    const auto members = groupMembers(groupKey);
    if (members.isEmpty()) return;

    QJsonArray peers;
    for (const Session* s : members) {
        peers.append(QJsonObject{
            {"PlayerId", s->playerId},
            {"Name",     s->name},
            {"Pos",      QJsonArray{s->pos[0], s->pos[1], s->pos[2]}},
            {"Facing",   QJsonArray{s->facing[0], s->facing[1], s->facing[2]}},
        });
    }

    const QString msg = toJson({{"Type", "peers"}, {"Peers", peers}});
    for (const Session* s : members)
        sendText(s->ws, msg);
}

// ---------------------------------------------------------------------------

void RelayServer::sendText(QWebSocket* ws, const QString& message) {
    if (ws && ws->isValid())
        ws->sendTextMessage(message);
}

void RelayServer::sendBinary(QWebSocket* ws, const QByteArray& data) {
    if (ws && ws->isValid())
        ws->sendBinaryMessage(data);
}

void RelayServer::addToGroup(QWebSocket* ws, const QString& key) {
    if (!key.isEmpty())
        m_groups[key].insert(ws);
}

void RelayServer::removeFromGroup(QWebSocket* ws, const QString& key) {
    if (key.isEmpty()) return;
    auto it = m_groups.find(key);
    if (it == m_groups.end()) return;
    it->remove(ws);
    if (it->isEmpty())
        m_groups.erase(it);
}

QList<Session*> RelayServer::groupMembers(const QString& groupKey, QWebSocket* exclude) const {
    QList<Session*> result;
    const auto it = m_groups.constFind(groupKey);
    if (it == m_groups.constEnd()) return result;
    for (QWebSocket* ws : *it) {
        if (ws == exclude) continue;
        Session* s = m_sessions.value(ws);
        if (s && s->hasIdentity())
            result.append(s);
    }
    return result;
}
