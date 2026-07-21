---
id: T7
title: Struct/record test-data builder & boundary-generation design
type: grilling
status: open
assignee:
blocked_by: [T1]
---

## Question

Tests need helpers to construct wire-format structs/records at boundary
sizes (empty, max-count, max-string-length) rather than hand-writing
literals per test, generically for any payload/schema test.

Decide the builder API shape once `STRUCT`/`ARRAY` support lands per T1
(type-system gap prioritization & sequencing): does it introspect a
`STRUCT` `PouAst`'s declared fields to generate boundary variants
automatically, or require a per-struct builder registration? Decide what
"boundary" means per field type (numeric min/max, string max-length,
array empty/max-count) and how a test asks for a specific boundary
combination vs "give me all boundary permutations." Also note in the
resolution whether this shares a serialization/wire-format layer with the
loopback transport (T4, in-process transport loopback design) or is
independent — flagged in this map's "Not yet specified" for now.
