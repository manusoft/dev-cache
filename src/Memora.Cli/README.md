# Memora CLI

<img width="512" height="512" alt="memora_icon (Custom)" src="https://github.com/user-attachments/assets/a4153c53-05f0-4458-9906-f7dfb18d959d" />
 
**Memora CLI** is the command-line client for **Memora**, a lightweight Redis-compatible in-memory data store. It allows developers to interact with the Memora server using an interactive shell (REPL) or single-command execution mode.

The CLI is designed for simplicity, fast debugging, and scripting during development.

---

## Features

* Interactive REPL mode
* One-shot command execution
* Redis-style command syntax
* TCP connection to Memora server
* Lightweight and fast startup
* Script-friendly output

---

## Requirements

* [.NET SDK](https://dotnet.microsoft.com/) (recommended .NET 8 or later)
* Running Memora Server instance

---

## Build

Clone the repository from GitHub:

```bash
git clone https://github.com/manusoft/Memora.git
cd Memora
```

Build the CLI:

```bash
dotnet build src/Memora.Cli
```

---

## Run

### Interactive Mode (REPL)

```bash
dotnet run --project src/Memora.Cli
```

Example session:

```
memora-cli> KEYS
  1) sample_session:234567890
  2) sample_session:123456789
  3) sample_jobQueue:waitingList
  4) sample_session:345678901
  5) sample_session:901234567
  6) sample_session:778899001
  7) sample_session:678901234
  8) sample_session:112233445
  9) sample_jobQueue:ticket:101
  10) sample_jobQueue:ticket:102
  11) sample_jobQueue:ticket:103
  12) sample_session:456789012
  13) sample_session:890123456
  14) sample_session:012345678
  15) sample_session:567890123
  16) sample_session:990011223
  17) sample_session:789012345
  18) sample_session:334455667
  19) sample_session:556677889
memora-cli> SET name "Memora"
"OK"
memora-cli> GET name
"Memora"
```

---

### One-Shot Mode

Run a single command directly:

```bash
dotnet run --project src/Memora.Cli SET foo bar
```

Example:

```
OK
```

This mode is useful for:

* automation scripts
* CI pipelines
* quick debugging

---

## Connection Options

Default connection:

```
127.0.0.1:6380
```

If supported by your implementation, host and port may be passed:

```bash
memora-cli --host 127.0.0.1 --port 6380
```

---

## Supported Commands

Memora CLI forwards commands directly to the Memora server. Supported commands depend on server implementation but typically include:

### Strings

* `SET`
* `GET`
* `DEL`
* `EXISTS`
* `INCR`
* `INCRBY`

### TTL

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

### Server

* `INFO`
* `FLUSHDB`
* `FLUSHALL`

---

## Example Workflow

Start Memora server:

```bash
dotnet run --project src/Memora.Server
```

Open CLI:

```bash
dotnet run --project src/Memora.Cli
```

Run commands:

```
SET user:1 "Manoj"
GET user:1
```

---

## CLI Architecture

```
User Input
   ↓
Command Parser
   ↓
RESP Client
   ↓
Memora Server
```

The CLI communicates using the Redis RESP protocol and does not directly depend on internal server classes, making it suitable for testing and external tooling.

---

## Development

Built using **.NET** from Microsoft.

Run in development mode:

```bash
dotnet run --project src/Memora.Cli
```

---

## Future Improvements (Suggested)

* Command auto-completion
* Syntax highlighting
* Command history persistence
* JSON output mode
* Script file execution (`memora run script.mem`)

---

## License

MIT License
