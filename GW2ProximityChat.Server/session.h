#pragma once
#include <QString>

class QWebSocket;

struct Session {
    QWebSocket* ws           = nullptr;
    QString     playerId;
    QString     name;
    int         mapId        = 0;
    QString     instanceKey;
    float       pos[3]       = {};
    float       facing[3]    = {};
    qint64      lastSeenMs   = 0;

    // Audio activity — used for logging only, not relay logic.
    qint64 lastAudioAt          = 0;
    int    audioFramesInWindow  = 0;
    int    audioBytesInWindow   = 0;

    bool    hasIdentity() const { return !playerId.isEmpty(); }
    QString groupKey()    const {
        return instanceKey.isEmpty()
            ? QString()
            : QString::number(mapId) + QLatin1Char(':') + instanceKey;
    }
    QString label() const {
        return playerId.isEmpty()
            ? QStringLiteral("<unidentified>")
            : playerId + QLatin1Char('(') + name + QLatin1Char(')');
    }
};
