#include "config.h"
#include <QCoreApplication>
#include <QFile>
#include <QFileInfo>
#include <QSettings>
#include <QTextStream>

QString Config::path() {
    return QCoreApplication::applicationDirPath() + QStringLiteral("/server.cfg");
}

bool Config::exists() {
    return QFileInfo::exists(path());
}

Config Config::load() {
    QSettings s(path(), QSettings::IniFormat);
    Config c;
    s.beginGroup(QStringLiteral("Server"));
    c.serverName = s.value(QStringLiteral("name"),       c.serverName).toString();
    c.password   = s.value(QStringLiteral("password"),   c.password).toString();
    c.port       = s.value(QStringLiteral("port"),       c.port).toInt();
    c.userLimit  = s.value(QStringLiteral("user_limit"), c.userLimit).toInt();
    s.endGroup();
    return c;
}

// Written manually so we can include comments. QSettings strips them.
void Config::writeDefaults() {
    QFile f(path());
    if (!f.open(QIODevice::WriteOnly | QIODevice::Text))
        return;

    QTextStream out(&f);
    out << "[Server]\n"
        << "\n"
        << "; Display name shown to clients on connect\n"
        << "name = GW2 Proximity Chat Relay\n"
        << "\n"
        << "; Connection password. Leave empty to allow anyone.\n"
        << "password = \n"
        << "\n"
        << "; TCP port to listen on\n"
        << "port = 5847\n"
        << "\n"
        << "; Maximum simultaneous connections (0 = unlimited)\n"
        << "user_limit = 0\n";
}
