# Iteration 130 — SSH operator tool

- The local operator uses administrator-configured SSH access for Ubuntu hosts, streams Bootstrap credentials over stdin and warns that cleanup removes all Docker data on the selected server.
- Emergency access uses a locally managed SSH tunnel; the operator does not provision keys or enable insecure Auth HTTP.
