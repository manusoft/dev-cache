# Memora

![Static Badge](https://img.shields.io/badge/ManuHub.Memora.Client-red)
![NuGet Version](https://img.shields.io/nuget/v/ManuHub.Memora.Client)
![NuGet Downloads](https://img.shields.io/nuget/dt/ManuHub.Memora.Client)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE.txt)
![Visitors](https://visitor-badge.laobi.icu/badge?page_id=manusoft/Memora)

<img width="512" height="512" alt="Design a product lan (Custom)" src="https://github.com/user-attachments/assets/0ae7f4b4-c6b0-41b9-8bcf-2111e7aaa478" />

Memora is a lightweight, Redis-inspired in-memory data store built for **learning, experimentation, and developer-focused caching**. It implements a subset of Redis semantics with a strong emphasis on **correct behavior, clean architecture, and protocol compatibility**.

This project is intentionally designed to explore *how Redis works internally* — not just at the command level, but across networking, persistence, expiry, and tooling.

---

## ⬇️ Downloads
- [Windows](https://github.com/manusoft/Memora/releases/download/v1.0.0/memora-win-x64.zip)
- [Linux](https://github.com/manusoft/Memora/releases/download/v1.0.0/memora-linux-x64.zip)

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
│  Memora  │◀────────────▶│   CLI        │
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

Memora uses an **Append-Only File (AOF)** similar to Redis:

* Every mutating command is appended in RESP format
* On startup, the AOF is replayed to reconstruct state
* Background AOF rewrite compacts the log
* Rewrite skips expired keys and replays logical state

This provides durability while keeping the implementation approachable.

---

## 🚀 Getting Started

### Run the server

```bash
dotnet run --project Memora.Server
```

### Use the CLI

```bash
dotnet run --project Memora.Cli
```

Or execute a single command:

```bash
dotnet run --project Memora.Cli SET foo bar
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

## 📚 Why Memora?

Memora exists to answer one question:

> *How does Redis actually work under the hood?*

On **Microsoft Windows**, setting up Redis for development often requires **Docker** or **WSL**.

### Memora provides a simpler approach:
- Download release
- Run memora-server.exe
- Start developing immediately

> No containers or external services required.

### Memora is ideal for:
- Local development
- Integration testing
- Prototyping
- Lightweight production usage

---

## ⚠️ Disclaimer

Memora **is not intended to fully replace Redis** for high-scale production. It implements a compatible wire protocol for development purposes.

---

## 📄 License

MIT License
