#!/bin/sh
# Stand-in for the Unity server. The lobby starts it with the same arguments as the real binary.
exec node "$(dirname "$0")/fake-room-server.mjs" "$@"
