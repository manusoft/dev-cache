using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevCache.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace DevCache.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string Host = "127.0.0.1";
    private const int Port = 6380;

    [ObservableProperty]
    private ObservableCollection<CacheEntryViewModel> entries = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedEntry))]
    private CacheEntryViewModel? selectedEntry;

    [ObservableProperty]
    private string status = "Ready";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string keyText;

    [ObservableProperty]
    private string valueText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpiryMode))]
    private bool isNewOrEditMode;

    [ObservableProperty]
    private int expireSeconds = 3600;  // default 1 hour

    [ObservableProperty]
    public bool isPaneOpen = false;

    public bool HasSelectedEntry => SelectedEntry != null;

    public bool IsExpiryMode => !IsNewOrEditMode;

    public MainViewModel()
    {
        KeyText = string.Empty;
        ValueText = string.Empty;

        _ = RefreshAsync(); // initial load
    }


    [RelayCommand]
    private async Task ClosePane()
    {
        SelectedEntry = null;
        IsPaneOpen = false;
    }

    [RelayCommand]
    private async Task AddKey()
    {
        SelectedEntry = null;
        //IsPaneOpen = true;
    }

    partial void OnSelectedEntryChanged(CacheEntryViewModel? value)
    {
        if (value != null && !string.IsNullOrEmpty(value.Key)) // Edit mode
        {
            IsPaneOpen = true;
            IsNewOrEditMode = true;
            KeyText = value.Key;
            _ = LoadValueAsync(value);
            return;
        }
        else // New mode
        {
            IsPaneOpen = true;
            IsNewOrEditMode = true;
            KeyText = "";
            ValueText = "";
        }
    }

    private async Task LoadValueAsync(CacheEntryViewModel? entry)
    {
        if (entry == null) return;

        try
        {
            using var client = new TcpClient(Host, Port);
            using var stream = client.GetStream();
            var writer = new RespWriter(stream);
            var reader = new RespReader(stream);

            await writer.WriteAsync(RespValue.Array(new[]
            {
                RespValue.BulkString("GET"),
                RespValue.BulkString(entry.Key)
            }));

            var resp = await reader.ReadAsync();
            entry.Value = resp?.Type switch
            {
                RespType.BulkString => (string?)resp.Value,
                RespType.NullBulk => "(nil)",
                _ => "(error)"
            };

            ValueText = entry.Value ?? "(connection error)";
        }
        catch
        {
            entry.Value = "(connection error)";
        }
    }


    partial void OnExpireSecondsChanged(int value)
    {
        if (value < 1) ExpireSeconds = 1;
    }

    [RelayCommand]
    private void OpenExpireTip()
    {
        ExpireSeconds = 3600; // reset to default each time, or keep last used
    }


    [RelayCommand]
    private async Task ExpireKeyConfirmAsync()
    {
        if (SelectedEntry == null)
        {
            Status = "No key selected";
            return;
        }

        int seconds = ExpireSeconds;

        if (seconds < 1)
        {
            Status = "Expiration time must be at least 1 second";
            return;
        }

        IsBusy = true;
        Status = $"Applying EXPIRE {seconds}s to {SelectedEntry.Key}...";

        try
        {
            using var client = new TcpClient(Host, Port);
            using var stream = client.GetStream();
            var writer = new RespWriter(stream);
            var reader = new RespReader(stream);

            await writer.WriteAsync(RespValue.Array(new[]
            {
                RespValue.BulkString("EXPIRE"),
                RespValue.BulkString(SelectedEntry.Key),
                RespValue.BulkString(seconds.ToString())
            }));

            var response = await reader.ReadAsync();

            if (response?.Type == RespType.Integer)
            {
                long result = (long)response.Value!;
                if (result == 1)
                {
                    Status = $"Expiration set to {seconds} seconds";
                    await RefreshAsync();  // update TTL in list
                }
                else
                {
                    Status = "Failed to set expiration (key may not exist)";
                }
            }
            else
            {
                Status = "Unexpected response from EXPIRE";
            }
        }
        catch (Exception ex)
        {
            Status = $"Expire error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        Status = "Loading keys...";

        try
        {
            Entries.Clear();

            using var client = new TcpClient(Host, Port);
            using var stream = client.GetStream();

            var writer = new RespWriter(stream);
            var reader = new RespReader(stream);

            // Send KEYS *
            await writer.WriteAsync(RespValue.Array(new[]
            {
                RespValue.BulkString("KEYS"),
                RespValue.BulkString("*")
            }));

            var response = await reader.ReadAsync();
            if (response?.Type != RespType.Array) throw new Exception("Invalid KEYS response");

            var keys = (IReadOnlyList<RespValue>)response.Value!;

            foreach (var keyResp in keys)
            {
                if (keyResp.Type != RespType.BulkString) continue;
                var key = (string)keyResp.Value!;

                // Get meta
                await writer.WriteAsync(RespValue.Array(new[]
                {
                    RespValue.BulkString("GETMETA"),
                    RespValue.BulkString(key)
                }));

                var metaResp = await reader.ReadAsync();
                if (metaResp?.Type != RespType.Array) continue;

                var metaItems = (IReadOnlyList<RespValue>)metaResp.Value!;
                if (metaItems.Count < 3) continue;

                var entry = new CacheEntryViewModel
                {
                    Key = key,
                    Type = (string?)metaItems[0].Value ?? "unknown",
                    TtlSeconds = (long?)metaItems[1].Value is long l ? (int)l : -1,
                    SizeBytes = (long?)metaItems[2].Value is long s ? (int)s : 0
                };

                Entries.Add(entry);
            }

            Status = $"Loaded {Entries.Count} keys";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }


    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedEntry == null)
        {
            Status = "No key selected to delete";
            return;
        }

        IsBusy = true;
        Status = $"Deleting {SelectedEntry.Key}...";

        try
        {
            using var client = new TcpClient(Host, Port);
            using var stream = client.GetStream();
            var writer = new RespWriter(stream);
            var reader = new RespReader(stream);

            // Send DEL key
            await writer.WriteAsync(RespValue.Array(new[]
            {
            RespValue.BulkString("DEL"),
            RespValue.BulkString(SelectedEntry.Key)
        }));

            var response = await reader.ReadAsync();
            if (response?.Type == RespType.Integer && (long)response.Value! == 1)
            {
                Status = $"Deleted {SelectedEntry.Key}";
                Entries.Remove(SelectedEntry);
                SelectedEntry = null; // clear selection
            }
            else
            {
                Status = "Delete failed (key may not exist)";
            }
        }
        catch (Exception ex)
        {
            Status = $"Delete error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetKeyConfirmAsync()
    {
        string key = KeyText?.Trim() ?? "";
        string value = ValueText?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(key))
        {
            Status = "Key cannot be empty";
            return;
        }

        IsBusy = true;
        Status = $"Setting {key}...";

        try
        {
            using var client = new TcpClient(Host, Port);
            using var stream = client.GetStream();
            var writer = new RespWriter(stream);
            var reader = new RespReader(stream);

            // Send SET key value
            await writer.WriteAsync(RespValue.Array(new[]
            {
                RespValue.BulkString("SET"),
                RespValue.BulkString(key),
                RespValue.BulkString(value)
            }));

            var response = await reader.ReadAsync();
            if (response?.Type == RespType.SimpleString && (string)response.Value! == "OK")
            {
                Status = $"Set {key} successfully";
                await RefreshAsync(); // reload list
            }
            else
            {
                Status = "SET failed";
            }
        }
        catch (Exception ex)
        {
            Status = $"SET error: {ex.Message}";
        }
        finally
        {
            IsPaneOpen = false;
            KeyText = string.Empty;
            ValueText = string.Empty;
            IsBusy = false;
        }
    }


    [RelayCommand]
    private async Task FlushDbAsync()
    {
        // Similar pattern: send FLUSHDB, then RefreshAsync()
        IsBusy = true;
        Status = $"Flushing Database...";

        try
        {
            using var client = new TcpClient(Host, Port);
            using var stream = client.GetStream();
            var writer = new RespWriter(stream);
            var reader = new RespReader(stream);

            // Send DEL key
            await writer.WriteAsync(RespValue.Array(new[]
            {
                RespValue.BulkString("FLUSHDB")
            }));

            var response = await reader.ReadAsync();
            if (response?.Type == RespType.Integer && (long)response.Value! == 1)
            {
                SelectedEntry = null; // clear selection
            }
            else
            {
                Status = "Flushing database failed.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Flushing error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

}