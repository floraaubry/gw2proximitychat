#pragma once
#include <QObject>
#include <QHash>
#include <QSet>
#include "config.h"
#include "session.h"

class QWebSocketServer;
class QWebSocket;
class QTimer;

class RelayServer : public QObject {
    Q_OBJECT
public:
    explicit RelayServer(const Config& config, bool verbose, QObject* parent = nullptr);
    ~RelayServer() override;

    bool listen();

private:
    void handleTextMessage(QWebSocket* ws, const QString& message);
    void handleBinaryMessage(QWebSocket* ws, const QByteArray& data);
    void handleDisconnect(QWebSocket* ws);
    void cleanupStale();
    void flushDirtyRosters();
    void broadcastRoster(const QString& groupKey);
    void sendText(QWebSocket* ws, const QString& message);
    void sendBinary(QWebSocket* ws, const QByteArray& data);
    void addToGroup(QWebSocket* ws, const QString& key);
    void removeFromGroup(QWebSocket* ws, const QString& key);
    QList<Session*> groupMembers(const QString& groupKey, QWebSocket* exclude = nullptr) const;

    Config           m_config;
    bool             m_verbose;
    int              m_connectionCount = 0;

    QWebSocketServer*               m_server  = nullptr;
    QHash<QWebSocket*, Session*>    m_sessions;
    QHash<QString, QSet<QWebSocket*>> m_groups;
    QSet<QString>                   m_dirtyGroups;

    QTimer* m_cleanupTimer = nullptr;
    QTimer* m_rosterTimer  = nullptr;
};
