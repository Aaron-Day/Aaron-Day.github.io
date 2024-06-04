using AaronDay.Models;
using Microsoft.AspNetCore.Components;
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
    private List<string> MechanicsFilter { get; set; } = new();
    private List<string> CategoriesFilter { get; set; } = new();
    private bool MatchAllMechanicsTags { get; set; } = true;
    private bool MatchAllCategoriesTags { get; set; } = true;
    private bool showFilterDialog = false;
    private List<string> UniqueMechanics => GetUniqueMechanics();
    private List<string> UniqueCategories => GetUniqueCategories();

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

        // Filter by mechanics and categories
        if(MechanicsFilter.Count > 0 || CategoriesFilter.Count > 0)
        {
            filtered = filtered.Where(g =>
            {
                bool mechanicsMatch = MatchAllMechanicsTags
                    ? MechanicsFilter.All(m => g.Mechanics.Contains(m))
                    : MechanicsFilter.Any(m => g.Mechanics.Contains(m));

                bool categoriesMatch = MatchAllCategoriesTags
                    ? CategoriesFilter.All(c => g.Categories.Contains(c))
                    : CategoriesFilter.Any(c => g.Categories.Contains(c));

                return mechanicsMatch && categoriesMatch;
            }).ToList();
        }

        return filtered.OrderBy(g => g.Handle).ToList();
    }

    protected static void ToggleDescriptionExpansion(Game game)
    {
        game.Expanded = !game.Expanded;
    }

    private void ToggleFilterDialog()
    {
        showFilterDialog = !showFilterDialog;
    }

    private List<string> GetUniqueMechanics()
    {
        return GameInventory.SelectMany(g => g.Mechanics).Distinct().OrderBy(m => m).ToList();
    }

    private List<string> GetUniqueCategories()
    {
        return GameInventory.SelectMany(g => g.Categories).Distinct().OrderBy(c => c).ToList();
    }

    private void ToggleMechanicTag(string mechanic)
    {
        if(MechanicsFilter.Contains(mechanic))
        {
            MechanicsFilter.Remove(mechanic);
        }
        else
        {
            MechanicsFilter.Add(mechanic);
        }
    }

    private void ToggleCategoryTag(string category)
    {
        if(CategoriesFilter.Contains(category))
        {
            CategoriesFilter.Remove(category);
        }
        else
        {
            CategoriesFilter.Add(category);
        }
    }
}
