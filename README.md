# DevCache

DevCache is a lightweight, Redis-inspired in-memory data store built for **learning, experimentation, and developer-focused caching**. It implements a subset of Redis semantics with a strong emphasis on **correct behavior, clean architecture, and protocol compatibility**.

This project is intentionally designed to explore *how Redis works internally* — not just at the command level, but across networking, persistence, expiry, and tooling.

---

## ✨ Features

### Core

* RESP2 protocol compatible (works with redis-cli-style clients)
* In-memory key-value store
* String, List, and Hash data types
* TTL with lazy + active expiry
* Accurate Redis-style error semantics

### Persistence

* Append-Only File (AOF) persistence
* Safe AOF replay on startup
* Background AOF rewrite (compaction)

### Server

* Async TCP server
* Concurrent clients
* Graceful shutdown
* Structured logging

### CLI

* Interactive REPL mode
* One-shot command execution
* Command history & autocomplete
* Redis-like output formatting

---

## 🧠 Architecture Overview

```
┌────────────┐     RESP      ┌──────────────┐
│  DevCache  │◀────────────▶│   CLI        │
│  Server    │               └──────────────┘
│            │
│  ┌──────┐  │               ┌──────────────┐
│  │ RESP │◀─┼──────────────▶│   Clients    │
│  │ I/O  │  │               └──────────────┘
│  └──────┘  │
│      │     │
│  ┌──────────────┐
│  │ InMemoryStore│
│  │  + TTL       │
│  │  + AOF       │
│  └──────────────┘
└────────────┘
```

Each layer is isolated:

* **Protocol** knows nothing about storage
* **Storage** knows nothing about networking
* **CLI** uses the protocol, not shortcuts

---

## 🗂️ Supported Commands (Partial)

### Strings

* `SET`, `GET`, `DEL`, `EXISTS`
* `INCR`, `INCRBY`

### TTL

* `EXPIRE`, `PEXPIRE`
* `TTL`, `PTTL`

### Lists

* `LPUSH`, `RPUSH`
* `LPOP`, `RPOP`
* `LLEN`

### Hashes

* `HSET`, `HGET`, `HDEL`
* `HLEN`, `HKEYS`, `HVALS`

### Server / Meta

* `INFO`
* `CONFIG GET`
* `FLUSHDB`, `FLUSHALL`

---

## 💾 Persistence Model

DevCache uses an **Append-Only File (AOF)** similar to Redis:

* Every mutating command is appended in RESP format
* On startup, the AOF is replayed to reconstruct state
* Background AOF rewrite compacts the log
* Rewrite skips expired keys and replays logical state

This provides durability while keeping the implementation approachable.

---

## 🚀 Getting Started

### Run the server

```bash
dotnet run --project DevCache.Server
```

### Use the CLI

```bash
dotnet run --project DevCache.Cli
```

Or execute a single command:

```bash
dotnet run --project DevCache.Cli SET foo bar
```

---

## 🎯 Design Goals

* Correct Redis-like behavior over raw performance
* Clear separation of concerns
* Learn-by-building internal systems
* Easy to extend (new commands, eviction, replication)

---

## 🛣️ Roadmap

* [ ] Eviction policies (LRU / LFU)
* [ ] Max memory limits
* [ ] Snapshot (RDB-lite) persistence
* [ ] Replication (master/replica)
* [ ] Benchmarks vs Redis

---

## 📚 Why DevCache?

DevCache exists to answer one question:

> *How does Redis actually work under the hood?*

This project explores protocol handling, concurrency, expiry, persistence, and tooling — without hiding complexity behind libraries.

---

## ⚠️ Disclaimer

DevCache is **not intended as a Redis replacement**. It is a learning-focused project and a demonstration of systems design principles.  
It implements a compatible wire protocol for development purposes.

---

## 📄 License

MIT License
