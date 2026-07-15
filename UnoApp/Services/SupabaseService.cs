using Supabase;
using Supabase.Gotrue;
using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;
using System.Text.Json;
using Client = Supabase.Client;

namespace UnoApp.Services;

// ═══════════════════════════════════════════════════════════════
// Postgrest Models (map to Supabase tables)
// ═══════════════════════════════════════════════════════════════

[Table("profiles")]
public class ProfileModel : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public string Id { get; set; } = "";

    [Column("display_name")]
    public string DisplayName { get; set; } = "";

    [Column("avatar_url")]
    public string? AvatarUrl { get; set; }

    [Column("is_cpu")]
    public bool IsCpu { get; set; }

    [Column("is_admin")]
    public bool IsAdmin { get; set; }

    [Column("is_banned")]
    public bool IsBanned { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}

[Table("matches")]
public class MatchModel : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public string Id { get; set; } = "";

    [Column("player_count")]
    public int PlayerCount { get; set; }

    [Column("round_count")]
    public int RoundCount { get; set; }

    [Column("game_settings")]
    public string GameSettings { get; set; } = "{}";

    [Column("winner_id")]
    public string? WinnerId { get; set; }

    [Column("started_at")]
    public DateTimeOffset StartedAt { get; set; }

    [Column("ended_at")]
    public DateTimeOffset EndedAt { get; set; }
}

[Table("match_players")]
public class MatchPlayerModel : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public string Id { get; set; } = "";

    [Column("match_id")]
    public string MatchId { get; set; } = "";

    [Column("player_id")]
    public string PlayerId { get; set; } = "";

    [Column("player_name")]
    public string PlayerName { get; set; } = "";

    [Column("player_index")]
    public int PlayerIndex { get; set; }

    [Column("is_winner")]
    public bool IsWinner { get; set; }

    [Column("total_score")]
    public int TotalScore { get; set; }

    [Column("round_score")]
    public int RoundScore { get; set; }

    [Column("action_score")]
    public int ActionScore { get; set; }

    [Column("cards_remaining")]
    public int CardsRemaining { get; set; }

    [Column("final_placement")]
    public int FinalPlacement { get; set; }
}

[Table("player_stats")]
public class PlayerStatsModel : BaseModel
{
    [PrimaryKey("player_id", false)]
    [Column("player_id")]
    public string PlayerId { get; set; } = "";

    [Column("total_wins")]
    public int TotalWins { get; set; }

    [Column("total_losses")]
    public int TotalLosses { get; set; }

    [Column("total_games")]
    public int TotalGames { get; set; }

    [Column("total_score")]
    public int TotalScore { get; set; }

    [Column("best_score")]
    public int BestScore { get; set; }

    [Column("win_streak")]
    public int WinStreak { get; set; }

    [Column("best_win_streak")]
    public int BestWinStreak { get; set; }

    [Column("win_rate")]
    public double WinRate { get; set; }

    [Column("last_played_at")]
    public DateTimeOffset? LastPlayedAt { get; set; }
}

[Table("rule_presets")]
public class RulePresetModel : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public string Id { get; set; } = "";

    [Column("owner_id")]
    public string OwnerId { get; set; } = "";

    [Column("name")]
    public string Name { get; set; } = "Custom Preset";

    [Column("settings")]
    public string Settings { get; set; } = "{}";

    [Column("is_default")]
    public bool IsDefault { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Leaderboard DTO (profile + stats joined)
// ═══════════════════════════════════════════════════════════════

public class LeaderboardEntryDto
{
    public string PlayerId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public bool IsCpu { get; set; }
    public int TotalWins { get; set; }
    public int TotalLosses { get; set; }
    public int TotalGames { get; set; }
    public int TotalScore { get; set; }
    public int BestScore { get; set; }
    public int WinStreak { get; set; }
    public int BestWinStreak { get; set; }
    public double WinRate { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// SupabaseService
// ═══════════════════════════════════════════════════════════════

public class SupabaseService
{
    private Client? _client;
    private const string SUPABASE_URL = "https://cxvjqehtwdyiaskbsnon.supabase.co";
    private const string SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImN4dmpxZWh0d2R5aWFza2Jzbm9uIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODQxMDQ2MzYsImV4cCI6MjA5OTY4MDYzNn0.WhO0ExXTqXTURhW2tiiLenFZ93vv6ebMjCSC1vDWPxc";

    public bool IsInitialized => _client != null;
    public bool IsLoggedIn => _client?.Auth?.CurrentUser != null;
    public string? CurrentUserId => _client?.Auth?.CurrentUser?.Id;

    // ── Initialization ───────────────────────────────────────

    public async Task InitializeAsync()
    {
        if (_client != null) return;

        var options = new SupabaseOptions
        {
            AutoRefreshToken = true,
            AutoConnectRealtime = false
        };

        _client = new Client(SUPABASE_URL, SUPABASE_ANON_KEY, options);
        await _client.InitializeAsync();
    }

    // ── Authentication ───────────────────────────────────────

    public async Task<Supabase.Gotrue.Session?> SignInAnonymously()
    {
        EnsureInitialized();
        var session = await _client!.Auth.SignInAnonymously();
        return session;
    }

    public async Task<Supabase.Gotrue.Session?> SignInWithEmail(string email, string password)
    {
        EnsureInitialized();
        var session = await _client!.Auth.SignIn(Supabase.Gotrue.Constants.SignInType.Email, email, password);
        return session;
    }

    public async Task<Supabase.Gotrue.Session?> SignUp(string email, string password, string displayName)
    {
        EnsureInitialized();
        var options = new SignUpOptions
        {
            Data = new Dictionary<string, object>
            {
                { "display_name", displayName }
            }
        };
        var session = await _client!.Auth.SignUp(email, password, options);
        return session;
    }

    public async Task SignOut()
    {
        EnsureInitialized();
        await _client!.Auth.SignOut();
    }

    public User? GetCurrentUser() => _client?.Auth?.CurrentUser;

    // ── Profile ──────────────────────────────────────────────

    public async Task<ProfileModel?> GetProfile(string userId)
    {
        EnsureInitialized();
        var response = await _client!.From<ProfileModel>()
            .Where(p => p.Id == userId)
            .Single();
        return response;
    }

    public async Task<ProfileModel?> GetMyProfile()
    {
        var userId = CurrentUserId;
        if (userId == null) return null;
        return await GetProfile(userId);
    }

    public async Task UpdateDisplayName(string newName)
    {
        EnsureInitialized();
        var userId = CurrentUserId;
        if (userId == null) return;

        await _client!.From<ProfileModel>()
            .Where(p => p.Id == userId)
            .Set(p => p.DisplayName, newName)
            .Set(p => p.UpdatedAt, DateTimeOffset.UtcNow)
            .Update();
    }

    public async Task UpdateAvatarUrl(string avatarUrl)
    {
        EnsureInitialized();
        var userId = CurrentUserId;
        if (userId == null) return;

        await _client!.From<ProfileModel>()
            .Where(p => p.Id == userId)
            .Set(p => p.AvatarUrl, avatarUrl)
            .Set(p => p.UpdatedAt, DateTimeOffset.UtcNow)
            .Update();
    }

    // ── Avatar Upload ────────────────────────────────────────

    public async Task<string?> UploadAvatar(byte[] fileBytes, string fileName)
    {
        EnsureInitialized();
        var userId = CurrentUserId;
        if (userId == null) return null;

        var ext = Path.GetExtension(fileName).ToLower();
        var storagePath = $"{userId}/avatar{ext}";

        // Upload to the "avatars" bucket
        await _client!.Storage.From("avatars").Upload(
            fileBytes,
            storagePath,
            new Supabase.Storage.FileOptions
            {
                Upsert = true
            }
        );

        // Get the public URL
        var publicUrl = _client.Storage.From("avatars").GetPublicUrl(storagePath);

        // Update the profile with the new avatar URL
        await UpdateAvatarUrl(publicUrl);

        return publicUrl;
    }

    // ── Leaderboard (Global) ─────────────────────────────────

    public async Task<List<LeaderboardEntryDto>> GetGlobalLeaderboard(int top = 20)
    {
        EnsureInitialized();

        // Fetch top players by wins from player_stats
        var stats = await _client!.From<PlayerStatsModel>()
            .Order(s => s.TotalWins, Supabase.Postgrest.Constants.Ordering.Descending)
            .Limit(top)
            .Get();

        if (stats.Models == null || stats.Models.Count == 0)
            return new List<LeaderboardEntryDto>();

        // Fetch matching profiles
        var playerIds = stats.Models.Select(s => s.PlayerId).ToList();
        var profiles = await _client.From<ProfileModel>()
            .Filter("id", Supabase.Postgrest.Constants.Operator.In, playerIds)
            .Get();

        var profileMap = profiles.Models?.ToDictionary(p => p.Id) ?? new();

        // Combine into leaderboard entries
        var leaderboard = new List<LeaderboardEntryDto>();
        foreach (var stat in stats.Models)
        {
            profileMap.TryGetValue(stat.PlayerId, out var profile);
            leaderboard.Add(new LeaderboardEntryDto
            {
                PlayerId = stat.PlayerId,
                DisplayName = profile?.DisplayName ?? "Unknown",
                AvatarUrl = profile?.AvatarUrl,
                IsCpu = profile?.IsCpu ?? false,
                TotalWins = stat.TotalWins,
                TotalLosses = stat.TotalLosses,
                TotalGames = stat.TotalGames,
                TotalScore = stat.TotalScore,
                BestScore = stat.BestScore,
                WinStreak = stat.WinStreak,
                BestWinStreak = stat.BestWinStreak,
                WinRate = stat.WinRate
            });
        }

        return leaderboard;
    }

    // ── Player Stats ─────────────────────────────────────────

    public async Task<PlayerStatsModel?> GetPlayerStats(string playerId)
    {
        EnsureInitialized();
        var response = await _client!.From<PlayerStatsModel>()
            .Where(s => s.PlayerId == playerId)
            .Single();
        return response;
    }

    public async Task<PlayerStatsModel?> GetMyStats()
    {
        var userId = CurrentUserId;
        if (userId == null) return null;
        return await GetPlayerStats(userId);
    }

    // ── Match History ────────────────────────────────────────

    public async Task<List<MatchPlayerModel>> GetMatchHistory(string playerId, int limit = 20)
    {
        EnsureInitialized();
        var response = await _client!.From<MatchPlayerModel>()
            .Where(mp => mp.PlayerId == playerId)
            .Order(mp => mp.MatchId, Supabase.Postgrest.Constants.Ordering.Descending)
            .Limit(limit)
            .Get();
        return response.Models ?? new List<MatchPlayerModel>();
    }

    // ── Match Saving (called by host after MP game ends) ─────

    public async Task SaveMatchResult(
        int playerCount,
        int roundCount,
        string gameSettingsJson,
        string? winnerId,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        List<MatchPlayerData> players)
    {
        EnsureInitialized();

        // 1. Insert the match
        var match = new MatchModel
        {
            Id = Guid.NewGuid().ToString(),
            PlayerCount = playerCount,
            RoundCount = roundCount,
            GameSettings = gameSettingsJson,
            WinnerId = winnerId,
            StartedAt = startedAt,
            EndedAt = endedAt
        };

        await _client!.From<MatchModel>().Insert(match);

        // 2. Insert each player's result
        foreach (var p in players)
        {
            var mp = new MatchPlayerModel
            {
                Id = Guid.NewGuid().ToString(),
                MatchId = match.Id,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PlayerIndex = p.PlayerIndex,
                IsWinner = p.IsWinner,
                TotalScore = p.TotalScore,
                RoundScore = p.RoundScore,
                ActionScore = p.ActionScore,
                CardsRemaining = p.CardsRemaining,
                FinalPlacement = p.FinalPlacement
            };

            await _client.From<MatchPlayerModel>().Insert(mp);
        }
    }

    // ── Rule Presets ─────────────────────────────────────────

    public async Task<List<RulePresetModel>> GetMyPresets()
    {
        EnsureInitialized();
        var userId = CurrentUserId;
        if (userId == null) return new List<RulePresetModel>();

        var response = await _client!.From<RulePresetModel>()
            .Where(r => r.OwnerId == userId)
            .Order(r => r.CreatedAt, Supabase.Postgrest.Constants.Ordering.Descending)
            .Get();

        return response.Models ?? new List<RulePresetModel>();
    }

    public async Task SavePreset(string name, string settingsJson, bool isDefault = false)
    {
        EnsureInitialized();
        var userId = CurrentUserId;
        if (userId == null) return;

        // If setting as default, clear other defaults first
        if (isDefault)
        {
            var existing = await _client!.From<RulePresetModel>()
                .Where(r => r.OwnerId == userId)
                .Where(r => r.IsDefault == true)
                .Get();

            if (existing.Models != null)
            {
                foreach (var preset in existing.Models)
                {
                    await _client.From<RulePresetModel>()
                        .Where(r => r.Id == preset.Id)
                        .Set(r => r.IsDefault, false)
                        .Update();
                }
            }
        }

        var newPreset = new RulePresetModel
        {
            Id = Guid.NewGuid().ToString(),
            OwnerId = userId,
            Name = name,
            Settings = settingsJson,
            IsDefault = isDefault,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _client!.From<RulePresetModel>().Insert(newPreset);
    }

    public async Task DeletePreset(string presetId)
    {
        EnsureInitialized();
        await _client!.From<RulePresetModel>()
            .Where(r => r.Id == presetId)
            .Delete();
    }

    public async Task SetDefaultPreset(string presetId)
    {
        EnsureInitialized();
        var userId = CurrentUserId;
        if (userId == null) return;

        // Clear all defaults for this user
        var existing = await _client!.From<RulePresetModel>()
            .Where(r => r.OwnerId == userId)
            .Where(r => r.IsDefault == true)
            .Get();

        if (existing.Models != null)
        {
            foreach (var preset in existing.Models)
            {
                await _client.From<RulePresetModel>()
                    .Where(r => r.Id == preset.Id)
                    .Set(r => r.IsDefault, false)
                    .Update();
            }
        }

        // Set the new default
        await _client.From<RulePresetModel>()
            .Where(r => r.Id == presetId)
            .Set(r => r.IsDefault, true)
            .Update();
    }

    // ── CPU Profiles ─────────────────────────────────────────

    public async Task<List<ProfileModel>> GetCpuProfiles()
    {
        EnsureInitialized();
        var response = await _client!.From<ProfileModel>()
            .Where(p => p.IsCpu == true)
            .Get();
        return response.Models ?? new List<ProfileModel>();
    }

    // ── Helpers ──────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (_client == null)
            throw new InvalidOperationException("SupabaseService not initialized. Call InitializeAsync() first.");
    }
}

// ═══════════════════════════════════════════════════════════════
// Data class for match saving
// ═══════════════════════════════════════════════════════════════

public class MatchPlayerData
{
    public string PlayerId { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public int PlayerIndex { get; set; }
    public bool IsWinner { get; set; }
    public int TotalScore { get; set; }
    public int RoundScore { get; set; }
    public int ActionScore { get; set; }
    public int CardsRemaining { get; set; }
    public int FinalPlacement { get; set; }
}
