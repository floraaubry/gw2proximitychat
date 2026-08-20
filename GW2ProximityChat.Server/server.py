#!/usr/bin/env python3
"""GW2ProximityChat relay server.

Single WebSocket endpoint (any path -- the client always connects to /ws, but nothing
here depends on that). JSON text frames carry position state and the peer roster;
binary frames carry raw Opus audio. Clients are grouped by (map_id, instance_key);
state and audio are only ever relayed within a group, so instance filtering is a hard
cutoff enforced by group membership rather than a numeric gain of zero.

JSON keys are exact PascalCase (PlayerId, not playerId) to match the Blish HUD client,
which serializes with .NET's JavaScriptSerializer rather than a camelCase-by-default
library.
"""

import argparse
import asyncio
import json
import logging
import time

import websockets
from websockets.exceptions import ConnectionClosed

STALE_TIMEOUT_SECONDS = 15.0
CLEANUP_INTERVAL_SECONDS = 5.0
AUDIO_SILENCE_GAP_SECONDS = 1.0  # gap after which the next frame logs as "started talking"

logger = logging.getLogger("gw2proximitychat")

# Set from argparse in main(). SERVER_PASSWORD empty/None means auth is disabled --
# anyone can join, same as before this feature existed.
SERVER_NAME = "GW2 Proximity Chat Relay"
SERVER_PASSWORD = None


class Session:
    def __init__(self, ws):
        self.ws = ws
        self.player_id = ""
        self.name = ""
        self.map_id = 0
        self.instance_key = ""
        self.pos = [0.0, 0.0, 0.0]
        self.facing = [0.0, 0.0, 0.0]
        self.last_seen = time.monotonic()
        self.send_lock = asyncio.Lock()

        # Audio activity tracking, purely for logging -- not used for relay logic.
        self.last_audio_at = 0.0
        self.audio_frames_in_window = 0
        self.audio_bytes_in_window = 0

    def label(self):
        return f"{self.player_id}({self.name})" if self.player_id else "<unidentified>"

    @property
    def group_key(self):
        return (self.map_id, self.instance_key)

    @property
    def has_identity(self):
        return bool(self.player_id)


sessions: dict = {}  # websocket -> Session


def group_members(group_key, exclude=None):
    return [
        s for s in sessions.values()
        if s.has_identity and s.group_key == group_key and s is not exclude
    ]


async def send_safe(session, message):
    async with session.send_lock:
        try:
            await session.ws.send(message)
        except ConnectionClosed:
            pass


async def broadcast_roster(group_key):
    _, instance_key = group_key
    if not instance_key:
        return

    members = group_members(group_key)
    if not members:
        return

    peers = [
        {"PlayerId": s.player_id, "Name": s.name, "Pos": s.pos, "Facing": s.facing}
        for s in members
    ]
    message = json.dumps({"Type": "peers", "Peers": peers})
    await asyncio.gather(*(send_safe(s, message) for s in members))


async def handle_state_message(session, raw):
    try:
        data = json.loads(raw)
    except ValueError:
        return

    player_id = data.get("PlayerId")
    if not player_id:
        return

    had_identity = session.has_identity

    # Password is only checked at the moment a connection first identifies itself --
    # not on every subsequent state message, since identity (and therefore trust) is
    # already established for the rest of the connection's lifetime at that point.
    if not had_identity and SERVER_PASSWORD and data.get("Password") != SERVER_PASSWORD:
        logger.warning("Rejected %s: wrong password", player_id)
        await send_safe(session, json.dumps({"Type": "auth_failed", "Reason": "Invalid password"}))
        await session.ws.close()
        return

    previous_group = session.group_key

    session.player_id = player_id
    session.name = data.get("Name") or ""
    session.map_id = data.get("MapId") or 0
    session.instance_key = data.get("InstanceKey") or ""

    pos = data.get("Pos")
    if isinstance(pos, list) and len(pos) == 3:
        session.pos = pos

    facing = data.get("Facing")
    if isinstance(facing, list) and len(facing) == 3:
        session.facing = facing

    session.last_seen = time.monotonic()

    new_group = session.group_key

    if not had_identity:
        logger.info(
            "Identified %s: map=%s instance=%s",
            session.label(), session.map_id, session.instance_key,
        )
    elif previous_group != new_group:
        logger.info(
            "%s changed group: map=%s instance=%s -> map=%s instance=%s",
            session.label(), previous_group[0], previous_group[1], new_group[0], new_group[1],
        )

    logger.debug("State from %s: pos=%s facing=%s", session.label(), session.pos, session.facing)

    if had_identity and previous_group != new_group:
        await broadcast_roster(previous_group)

    await broadcast_roster(new_group)


async def handle_audio_message(session, payload: bytes):
    if not session.has_identity or not session.instance_key:
        logger.debug("Dropping audio frame from %s (no identity/instance yet, %d bytes)", session.label(), len(payload))
        return

    id_bytes = session.player_id.encode("utf-8")
    if len(id_bytes) > 255:
        return

    now = time.monotonic()
    if now - session.last_audio_at > AUDIO_SILENCE_GAP_SECONDS:
        logger.info("%s started talking", session.label())
    session.last_audio_at = now
    session.audio_frames_in_window += 1
    session.audio_bytes_in_window += len(payload)

    frame = bytes([len(id_bytes)]) + id_bytes + payload
    recipients = group_members(session.group_key, exclude=session)
    logger.debug("Audio from %s: %d bytes -> %d recipient(s)", session.label(), len(payload), len(recipients))
    await asyncio.gather(*(send_safe(r, frame) for r in recipients))


async def handler(websocket):
    session = Session(websocket)
    sessions[websocket] = session
    logger.info("Connection opened from %s", websocket.remote_address)

    # Sent unauthenticated, before any password check, so the client can show the
    # server's name even if the connection is about to be rejected for a bad password.
    await send_safe(session, json.dumps({"Type": "hello", "ServerName": SERVER_NAME}))

    try:
        async for message in websocket:
            if isinstance(message, str):
                await handle_state_message(session, message)
            else:
                await handle_audio_message(session, message)
    except ConnectionClosed as e:
        logger.info("Connection closed (%s): %s", session.label(), e)
    finally:
        sessions.pop(websocket, None)
        logger.info("Connection ended: %s", session.label())
        if session.has_identity:
            await broadcast_roster(session.group_key)


async def cleanup_loop():
    while True:
        await asyncio.sleep(CLEANUP_INTERVAL_SECONDS)

        now = time.monotonic()
        stale_groups = set()

        for ws, session in list(sessions.items()):
            if session.audio_frames_in_window:
                logger.info(
                    "Audio from %s: %d frames / %d bytes in the last %.0fs",
                    session.label(), session.audio_frames_in_window,
                    session.audio_bytes_in_window, CLEANUP_INTERVAL_SECONDS,
                )
                session.audio_frames_in_window = 0
                session.audio_bytes_in_window = 0

            if session.has_identity and now - session.last_seen > STALE_TIMEOUT_SECONDS:
                logger.info("%s timed out (no state for %.0fs), dropping", session.label(), now - session.last_seen)
                stale_groups.add(session.group_key)
                sessions.pop(ws, None)
                try:
                    await ws.close()
                except Exception:
                    pass

        for group_key in stale_groups:
            await broadcast_roster(group_key)


async def main():
    global SERVER_NAME, SERVER_PASSWORD

    parser = argparse.ArgumentParser(description="GW2ProximityChat relay server")
    parser.add_argument("port", nargs="?", type=int, default=5847)
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--name", default=SERVER_NAME, help="Server name shown to clients")
    parser.add_argument(
        "--password", default=None,
        help="Require clients to send this password to connect. Omit to allow anyone (default).",
    )
    parser.add_argument(
        "-v", "--verbose", action="store_true",
        help="log every state/audio frame (very noisy) instead of just connect/identify/talking events",
    )
    args = parser.parse_args()

    SERVER_NAME = args.name
    SERVER_PASSWORD = args.password

    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(asctime)s %(levelname)-7s %(message)s",
        datefmt="%H:%M:%S",
    )
    # websockets' own connection-handshake logging is noisy at INFO; keep it at WARNING
    # unless -v was requested.
    if not args.verbose:
        logging.getLogger("websockets").setLevel(logging.WARNING)

    cleanup_task = asyncio.create_task(cleanup_loop())

    # No protocol-level ping/pong: dead-peer detection is already covered by the
    # (PlayerId-bearing) state timeout in cleanup_loop, and this removes one more
    # place where an automatic frame could collide with an app-level send on a
    # client whose WebSocket implementation doesn't fully serialize the two --
    # observed in practice as clients dropping every few seconds for no network reason.
    async with websockets.serve(handler, args.host, args.port, ping_interval=None):
        logger.info(
            "GW2ProximityChat relay server '%s' listening on ws://%s:%d/ (password %s)",
            SERVER_NAME, args.host, args.port, "required" if SERVER_PASSWORD else "not required",
        )
        try:
            await asyncio.Future()
        finally:
            cleanup_task.cancel()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
