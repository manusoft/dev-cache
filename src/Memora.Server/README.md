# Memora Server

<img width="512" height="512" alt="memora_icon (Custom)" src="https://github.com/user-attachments/assets/a4153c53-05f0-4458-9906-f7dfb18d959d" />

**Memora Server** is a lightweight Redis-compatible in-memory data store built for development, testing, and small production workloads. It implements the **RESP protocol** and core Redis command semantics, allowing existing Redis tools and clients to work with minimal configuration.

Memora is designed especially for **Windows-first .NET development environments**, providing a simple executable without requiring Docker or WSL.

---

## Features

* Redis-compatible **RESP2 protocol**
* In-memory key-value engine
* TTL support (key expiration)
* Append Only File (AOF) persistence
* Lightweight native executable
* Designed for development and small production workloads
* Compatible with Redis clients and tools

---

## Redis Tool Compatibility

Memora is compatible with **Redis Insight**, the official GUI for Redis.

This allows you to:

* Browse keys visually
* Inspect values
* Run commands
* Monitor memory usage
* Debug development environments easily

---

### Connect Using Redis Insight

1. Open **Redis Insight**
2. Click **Add Redis Database**
3. Use the default connection:

```
Host: 127.0.0.1
Port: 6380
```

4. Click **Connect**

Memora will appear like a standard Redis instance.

> Note: Some advanced Redis features may not be available depending on current Memora implementation.

---

## Quick Start

### Run Server

```bash
memora-server.exe
```

Default endpoint:

```
127.0.0.1:6380
```

---

### Test Using CLI

```bash
memora-cli
```

Example:

```
SET test hello
GET test
```

---

## Supported Data Types

Currently implemented Redis-style structures:

* Strings
* Lists
* Hashes

Additional structures may be added in future releases.

---

## Supported Commands (Core)

### Strings

* `SET`
* `GET`
* `DEL`
* `EXISTS`
* `INCR`
* `INCRBY`

### Expiration

* `EXPIRE`
* `TTL`
* `PTTL`

### Lists

* `LPUSH`
* `RPUSH`
* `LPOP`
* `RPOP`
* `LLEN`

### Hashes

* `HSET`
* `HGET`
* `HDEL`
* `HLEN`

### Server

* `INFO`
* `FLUSHDB`
* `FLUSHALL`

---

## Why Memora?

On **Microsoft Windows**, setting up Redis for development often requires Docker or WSL.

Memora provides a simpler approach:

* Download release
* Run `memora-server.exe`
* Start developing immediately

No containers or external services required.

Memora is ideal for:

* Local development
* Integration testing
* Prototyping
* Lightweight production usage

---

## Architecture Overview

```
Client / CLI / Redis Insight
            ↓
        RESP Protocol
            ↓
      Memora Command Layer
            ↓
     In-Memory Storage Engine
            ↓
         AOF Persistence
```

The modular structure makes Memora easy to understand and extend.

---

## Configuration (Planned / Optional)

Future versions may include:

* Config file support
* Memory limits
* Snapshot persistence
* Authentication

---

## Limitations

Memora is not intended to fully replace Redis for high-scale production.

Not currently implemented:

* Cluster mode
* Replication
* Pub/Sub
* Streams

---

## Development

Build from source:

```bash
dotnet build
```

Run:

```bash
dotnet run --project src/Memora.Server
```

---

## License

MIT License
