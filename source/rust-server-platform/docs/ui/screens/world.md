# World screen

| Property | Contract |
| --- | --- |
| Route | `/world` |
| Status | Current placeholder |
| Purpose | Establish the world-management domain boundary without implying UI support beyond the current placeholder. |

The screen currently provides no world-management actions. RustServer.Control
now has configuration and explicit wipe APIs, but this placeholder does not yet
surface them. It still provides no current-world data, timestamps, snapshot
list, backup, or restore action.
