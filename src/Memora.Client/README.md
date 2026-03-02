# Memora.Client

![Static Badge](https://img.shields.io/badge/ManuHub.Memora.Client-red)
![NuGet Version](https://img.shields.io/nuget/v/ManuHub.Memora.Client)
![NuGet Downloads](https://img.shields.io/nuget/dt/ManuHub.Memora.Client)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE.txt)

<img width="512" height="512" alt="Memora Icon" src="https://github.com/user-attachments/assets/a4153c53-05f0-4458-9906-f7dfb18d959d" />

**Memora.Client** is a .NET library for interacting with a running **Memora server**. It provides a high-level API wrapper over the Memora protocol so you can use strongly-typed commands, handle responses cleanly, and integrate Memora into your applications with minimal boilerplate.

It complements the **Memora CLI** and **Memora Server** by giving developers a programmatic client for caching, key/value operations, and data structure commands.

---
# ⭐ Why Memora package?

Memora was created to solve a practical development issue:
On **Microsoft Windows**, setting up a Redis-compatible environment often requires Docker, WSL, or external services. Many .NET developers prefer a lightweight native solution that runs directly on Windows.

Memora provides a simple alternative by shipping a **native executable server** and a **.NET client library** designed for development and lightweight deployments.

## Key Idea
Memora runs as a **local executable**:

```bash
 memora-server.exe
```

So developers only need to:
1. Download the release
2. Run the server
3. Use the client or CLI

No Docker, WSL, or external infrastructure is required.

---

## Ideal Use Cases
- Local development caching
- Integration testing
- Prototyping
- Small production workloads
- Offline development environments

Memora is **not intended to replace large-scale Redis deployments**, but to provide a simple, **self-contained Redis-style environment** for Windows-focused development.

---

## Table of Contents

- [Overview](#overview)  
- [Installation](#installation)  
- [Connecting to Memora](#connecting-to-memora)  
- [Basic Commands](#basic-commands)  
- [List Operations](#list-operations)  
- [Hash Operations](#hash-operations)  
- [Database Commands](#database-commands)  
- [Error Handling](#error-handling)  
- [ASP.NET Core Example](#aspnet-core-example)  
- [CLI Usage](#cli-usage)  
- [Best Practices](#best-practices)

---

## Overview

Memora is designed for **fast in-memory caching** with a simple API:

- Key/Value storage  
- List and hash operations  
- TTL (time-to-live) for keys  
- FlushDB / FlushAll commands  
- Compatible with **.NET async/await** patterns  

**Components:**

| Component | Description |
|-----------|-------------|
| Memora.Server | TCP-based cache server |
| Memora.Client | .NET client library (NuGet) |
| Memora.Cli | Console interface for testing |
| Memora.Core | Core cache engine (internal) |
| Memora.Common | Shared utilities/models (internal) |

---

## Installation

Install the **client** via NuGet:

```bash
dotnet add package ManuHub.Memora.Client --version 1.0.0
````

Or add as a project reference:

```xml
<ProjectReference Include="ManuHub.Memora.Client" Version="1.0.0" />
```

---

## Connecting to Memora

### Via Dependency Injection (ASP.NET Core)

```csharp
using Memora.Client;

builder.Services.AddSingleton<IMemoraClient>(sp =>
    new MemoraClient(host: "127.0.0.1", port: 6380));
```

### Direct Instantiation

```csharp
using var client = new MemoraClient("127.0.0.1", 6380);
```

---

## Basic Commands

| Method                  | Description        | Example                                                      |
| ----------------------- | ------------------ | ------------------------------------------------------------ |
| `SetAsync(key, value)`  | Sets a value       | `await client.SetAsync("name", "Alice");`                    |
| `GetAsync(key)`         | Gets a value       | `var name = await client.GetAsync("name");`                  |
| `DelAsync(key)`         | Deletes a key      | `await client.DelAsync("name");`                             |
| `ExistsAsync(key)`      | Checks existence   | `await client.ExistsAsync("name");`                          |
| `ExpireAsync(key, ttl)` | Sets TTL           | `await client.ExpireAsync("name", TimeSpan.FromMinutes(5));` |
| `TTLAsync(key)`         | Gets remaining TTL | `var seconds = await client.TTLAsync("name");`               |

---

## List Operations

| Method                          | Description         | Example                                                |
| ------------------------------- | ------------------- | ------------------------------------------------------ |
| `LPushAsync(key, values)`       | Push values to head | `await client.LPushAsync("users", "Alice", "Bob");`    |
| `RPushAsync(key, values)`       | Push values to tail | `await client.RPushAsync("users", "Charlie");`         |
| `LPopAsync(key)`                | Pop value from head | `var user = await client.LPopAsync("users");`          |
| `RPopAsync(key)`                | Pop value from tail | `var user = await client.RPopAsync("users");`          |
| `LLenAsync(key)`                | Get list length     | `var len = await client.LLenAsync("users");`           |
| `LRangeAsync(key, start, stop)` | Get list slice      | `var list = await client.LRangeAsync("users", 0, -1);` |

---

## Hash Operations

| Method                            | Description           | Example                                                           |
| --------------------------------- | --------------------- | ----------------------------------------------------------------- |
| `HSetAsync(key, fieldValuePairs)` | Set multiple fields   | `await client.HSetAsync("user:1", "name", "Alice", "age", "25");` |
| `HGetAsync(key, field)`           | Get single field      | `var name = await client.HGetAsync("user:1", "name");`            |
| `HDelAsync(key, field)`           | Delete field          | `await client.HDelAsync("user:1", "age");`                        |
| `HLenAsync(key)`                  | Number of fields      | `var count = await client.HLenAsync("user:1");`                   |
| `HGetAllAsync(key)`               | Get all fields/values | `var fields = await client.HGetAllAsync("user:1");`               |

---

## Database Commands

| Method               | Description                   | Example                                   |
| -------------------- | ----------------------------- | ----------------------------------------- |
| `FlushDbAsync()`     | Delete all keys in current DB | `await client.FlushDbAsync();`            |
| `FlushAllAsync()`    | Delete all keys in all DBs    | `await client.FlushAllAsync();`           |
| `KeysAsync(pattern)` | List keys matching pattern    | `var keys = await client.KeysAsync("*");` |

---

## Error Handling

All commands throw **`MemoraException`** on server errors:

```csharp
try
{
    await client.SetAsync("key", "value");
}
catch (MemoraException ex)
{
    Console.WriteLine($"Memora error: {ex.Message}");
}
```

---

## ASP.NET Core Example

```csharp
[ApiController]
[Route("[controller]")]
public class CacheController : ControllerBase
{
    private readonly IMemoraClient _cache;

    public CacheController(IMemoraClient cache) => _cache = cache;

    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key)
    {
        var value = await _cache.GetAsync(key);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpPost("{key}/{value}")]
    public async Task<IActionResult> Set(string key, string value)
    {
        bool success = await _cache.SetAsync(key, value);
        return success ? Ok() : StatusCode(500);
    }
}
```

---

## CLI Usage

If using `Memora.Cli`:

```bash
memora-cli> set name Alice
memora-cli> get name
memora-cli> lpush users Alice Bob
memora-cli> hset user:1 name Alice age 25
memora-cli> hgetall user:1
```

---

## Best Practices

1. Use **singleton client** per application.
2. Always use **async methods**.
3. Catch **MemoraException** for errors.
4. Use **TTL** for expiring keys.
5. Avoid blocking calls on TCP client — use `await`.

---

## 📜 License

MIT License – see [LICENSE](LICENSE.txt)

