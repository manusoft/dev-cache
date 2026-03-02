# Memora – In-Memory Cache for .NET

[![NuGet](https://img.shields.io/nuget/v/Memora.Client.svg)](https://www.nuget.org/packages/Memora.Client/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

Memora is a **high-performance, Redis-inspired in-memory cache** for .NET. It provides key/value storage, lists, hashes, TTL, and DB commands, with a **.NET client** ready for integration in ASP.NET Core, console apps, or background services.

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
dotnet add package Memora.Client --version 1.0.0
````

Or add as a project reference:

```xml
<ProjectReference Include="..\Memora.Client\Memora.Client.csproj" />
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
memora set name Alice
memora get name
memora lpush users Alice Bob
memora hset user:1 name Alice age 25
memora hgetall user:1
```

---

## Best Practices

1. Use **singleton client** per application.
2. Always use **async methods**.
3. Catch **MemoraException** for errors.
4. Use **TTL** for expiring keys.
5. Avoid blocking calls on TCP client — use `await`.

---

## License

MIT License – see [LICENSE](LICENSE)

