#include "config.h"
#include "server.h"
#include "version.h"
#include <QCoreApplication>
#include <QTextStream>

#ifdef Q_OS_WIN
#  include <windows.h>
static void enableAnsiOnWindows() {
    HANDLE h = GetStdHandle(STD_OUTPUT_HANDLE);
    DWORD mode = 0;
    if (GetConsoleMode(h, &mode))
        SetConsoleMode(h, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
    SetConsoleOutputCP(CP_UTF8);
}
#else
static void enableAnsiOnWindows() {}
#endif

static void printHelp(const char* exe) {
    QTextStream out(stdout);
    out << "Usage: " << exe << " [--verbose|-v] [--help|-h]\n"
        << "\n"
        << "All server settings are read from server.cfg next to the executable.\n"
        << "Run once to generate a default config, edit it, then run again.\n"
        << "\n"
        << "Options:\n"
        << "  --verbose, -v    Log every state and audio frame (very noisy)\n"
        << "  --help,    -h    Show this help and exit\n";
}

static void printBanner(const Config& config, bool verbose) {
    const char* RST  = "\033[0m";
    const char* BOLD = "\033[1m";
    const char* CYAN = "\033[96m";
    const char* DIM  = "\033[90m";
    const char* WHT  = "\033[97m";

    auto pad = [](const QString& s, int w) { return s.leftJustified(w, QLatin1Char(' ')); };

    QTextStream out(stdout);
    const QString sep(46, QLatin1Char('-'));

    out << "\n"
        << DIM << "  " << sep << RST << "\n"
        << "  " << BOLD << CYAN << "GW2 Proximity Chat Relay" << RST
        << DIM  << "  v" << AppVersion::String << RST << "\n"
        << DIM << "  " << sep << RST << "\n";

    auto row = [&](const char* label, const QString& value) {
        out << "  " << WHT << BOLD << pad(QString(label), 13) << RST
            << " " << value << "\n";
    };

    row("Name",       config.serverName);
    row("Port",       QString::number(config.port));
    row("Password",   config.password.isEmpty() ? QStringLiteral("(none)") : QStringLiteral("required"));
    row("User Limit", config.userLimit == 0 ? QStringLiteral("unlimited") : QString::number(config.userLimit));
    row("Verbose",    verbose ? QStringLiteral("yes") : QStringLiteral("no"));

    out << DIM << "  " << sep << RST << "\n\n";
    out.flush();
}

int main(int argc, char* argv[]) {
    enableAnsiOnWindows();

    QCoreApplication app(argc, argv);

    bool verbose = false;
    for (int i = 1; i < argc; ++i) {
        const QString arg = QString::fromLocal8Bit(argv[i]);
        if (arg == QStringLiteral("--help") || arg == QStringLiteral("-h")) {
            printHelp(argv[0]);
            return 0;
        }
        if (arg == QStringLiteral("--verbose") || arg == QStringLiteral("-v")) {
            verbose = true;
        }
    }

    if (!Config::exists()) {
        Config::writeDefaults();
        QTextStream out(stdout);
        out << "No config found — created default server.cfg next to the executable.\n"
            << "Edit it, then run the server again.\n";
        return 0;
    }

    const Config config = Config::load();
    printBanner(config, verbose);

    if (verbose)
        qputenv("QT_LOGGING_RULES", "*.debug=true");

    RelayServer server(config, verbose);
    if (!server.listen())
        return 1;

    QTextStream(stdout) << "Listening on ws://0.0.0.0:" << config.port << "/\n\n";

    return app.exec();
}
