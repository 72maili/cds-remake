using System.IO;
using System.Text.Json;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 에디터에서 고친 부하의 <b>기능 열셋 · 언어 열넷</b> — 판(<see cref="Support.Local.Models.Player.GameId"/>)마다
/// 인물 표 위에 덮어쓴다. 포크에서 더한 것이다.
/// </summary>
/// <remarks>
/// 부하의 기능 · 언어는 세이브가 아니라 <b>모든 판이 같이 쓰는</b> 인물 표(<see cref="PersonTable"/>)에 있다.
/// 표를 고쳐 적으면(<c>인물표-고친것.json</c>) 모든 판에 번지고, 그 뒤로는 같이 깔린 본이 안 읽혀
/// 원작의 인물 표 손질도 안 들어온다. 그래서 고친 것만 따로 <c>%APPDATA%\CdsHelper\mate-edits.json</c> 에
/// 판 표지별로 적어 두고, 표를 열 때마다(<see cref="PersonTable"/> 생성자) 지금 판의 것만 얹는다.
/// <para>
/// 지금 판은 <see cref="GameId"/> 로 안다 — <c>Game.World</c> 가 물을 때마다 맞춰 둔다. 비어 있으면(도구 앱처럼
/// 판이 없는 자리) 아무것도 안 얹는다 — 도구 앱이 표를 고쳐 적을 때 판의 것이 섞여 들지 않는다.
/// </para>
/// </remarks>
public static class MateEdits
{
    /// <summary>한 사람의 고친 값. 길이는 <see cref="PersonTable.SkillCount"/> · <see cref="PersonTable.LangCount"/> 다.</summary>
    public sealed record Entry(int[] Skills, int[] Languages);

    /// <summary>지금 판의 표지. 비면 덮어쓰지 않는다.</summary>
    public static string GameId { get; set; } = "";

    /// <summary>적어 두는 파일.</summary>
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CdsHelper", "mate-edits.json");

    private static Dictionary<string, Dictionary<int, Entry>>? _all;

    private static Dictionary<string, Dictionary<int, Entry>> All => _all ??= Load();

    /// <summary>그 판 그 사람의 고친 값. 없으면 null.</summary>
    public static Entry? Get(string gameId, int person) =>
        All.TryGetValue(gameId ?? "", out var map) && map.TryGetValue(person, out var e) ? e : null;

    /// <summary>그 판 그 사람의 값을 적어 둔다. 파일에 곧바로 남긴다.</summary>
    public static void Set(string gameId, int person, IReadOnlyList<int> skills, IReadOnlyList<int> languages)
    {
        if (string.IsNullOrEmpty(gameId)) return;
        if (!All.TryGetValue(gameId, out var map)) All[gameId] = map = [];
        map[person] = new Entry(Fit(skills, PersonTable.SkillCount), Fit(languages, PersonTable.LangCount));
        Save();
    }

    /// <summary>지금 판(<see cref="GameId"/>)의 고친 값을 줄들에 얹는다.</summary>
    public static void Apply(IEnumerable<PersonTable.Row> rows)
    {
        if (string.IsNullOrEmpty(GameId) || !All.TryGetValue(GameId, out var map) || map.Count == 0) return;
        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.Id, out var e)) continue;
            row.Skills = Fit(e.Skills, PersonTable.SkillCount);
            row.Languages = Fit(e.Languages, PersonTable.LangCount);
        }
    }

    /// <summary>길이를 맞추고 0~3 으로 자른 새 배열.</summary>
    private static int[] Fit(IReadOnlyList<int>? values, int length)
    {
        var fit = new int[length];
        for (int i = 0; i < length && values != null && i < values.Count; i++)
            fit[i] = Math.Clamp(values[i], 0, Support.Local.Models.Skill.MaxLevel);
        return fit;
    }

    private static Dictionary<string, Dictionary<int, Entry>> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, Dictionary<int, Entry>>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"[MateEdits] 읽지 못했습니다: {ex.Message}");
        }
        return [];
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(All, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[MateEdits] 적지 못했습니다: {ex.Message}");
        }
    }
}
