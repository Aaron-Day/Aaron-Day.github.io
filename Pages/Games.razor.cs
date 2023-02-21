using AaronDay.Models;
using Microsoft.AspNetCore.Components;
using System.Net.Http.Json;
using System.Text.Json;

namespace Aaron_Day.github.io.Pages;

public partial class Games : ComponentBase
{
    private List<Game> GameInventory { get; set; } = new();
    private List<Game> FilteredGames => GetFilteredGames();
    private string? NameFilter { get; set; }
    private int? AgeFilter { get; set; }
    private int? PlaytimeFilter { get; set; }
    private int? PlayersFilter { get; set; }

    protected override async Task OnInitializedAsync()
    {
        string json = await Http.GetStringAsync("data/games.json");
        var inventory = JsonSerializer.Deserialize<GameInventory>(json) ?? new GameInventory();
        GameInventory = inventory.Games.Where(x => !x.Categories.Contains("Expansion")).ToList();
    }

    public List<Game> GetFilteredGames()
    {
        List<Game> filtered = GameInventory ?? new();

        if(!string.IsNullOrWhiteSpace(NameFilter))
        {
            filtered = filtered.Where(g => !string.IsNullOrWhiteSpace(g.Name) && g.Name.Contains(NameFilter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (AgeFilter is not null)
        {
            filtered = filtered.Where(g => g.MinAge <= AgeFilter).ToList();
        }

        if (PlaytimeFilter is not null)
        {
            filtered = filtered.Where(g => g.MinPlaytime <= PlaytimeFilter).ToList();
        }

        if (PlayersFilter is not null)
        {
            filtered = filtered.Where(g => g.PlayerCounts.ToList().Contains((int)PlayersFilter)).ToList();
        }

        return filtered.OrderBy(g => g.Handle).ToList();
    }
}
