# GW2 Proximity Chat

A [Blish HUD](https://blishhud.com/) module for proximity voice chat in Guild Wars 2.

Two parts:

- **Client** (`GW2ProximityChat.csproj`) — the Blish HUD module. Windows only 
- **Server** (`GW2ProximityChat.Server/`) — a lightweight C++/Qt relay that groups
  players by map instance and forwards position + voice between them. 

## Server

Requires CMake and Qt6 (`Core`, `WebSockets`).

```
cd GW2ProximityChat.Server
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build
./build/GW2ProximityChatServer
```

On Debian/Ubuntu, `./build.sh` does the same and installs missing dependencies
(`build-essential cmake ninja-build qt6-base-dev libqt6websockets6-dev`) for you.

First run creates a `server.cfg` next to the executable and exits — edit it (server
name, password, port) and run again.

### Config options

| Key | Default | Description |
|---|---|---|
| `name` | `GW2 Proximity Chat Relay` | Display name shown to clients on connect. |
| `password` | *(empty)* | Connection password. Leave empty to allow anyone. |
| `port` | `5847` | TCP port to listen on. |
| `user_limit` | `0` | Maximum simultaneous connections (`0` = unlimited). |

## License

See [LICENSE](LICENSE).
