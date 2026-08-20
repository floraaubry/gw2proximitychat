#pragma once
#include <QString>

struct Config {
    QString serverName = QStringLiteral("GW2 Proximity Chat Relay");
    QString password;
    int     port      = 5847;
    int     userLimit = 0;   // 0 = unlimited

    static bool   exists();
    static Config load();
    static void   writeDefaults();
    static QString path();
};
