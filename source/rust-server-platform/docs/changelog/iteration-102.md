# Iteration 102 — Correct nftables IPv4 DNAT syntax

## Fixed

- NetworkAccess now emits `dnat ip to` for its IPv4 destination-NAT rule in
  the `inet` nftables table, allowing nftables on Linux to parse and apply the
  generated transaction.

## Validation

- Updated the Control nftables transaction test to assert the valid IPv4 DNAT
  rule syntax.
- Ubuntu nftables v1.1.6 accepted the generated IPv4 DNAT rule during a
  `--check` parser validation; the local WSL session did not have
  `CAP_NET_ADMIN`, so no rule was applied.
