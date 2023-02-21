using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AaronDay.Models;

public class GameBase
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string AtlasUrl { get; set; } = string.Empty;
    public int YearPublished { get; set; } = -1;
    public int MinPlayers { get; set; } = -1;
    public int MaxPlayers { get; set; } = -1;
    public int MinPlaytime { get; set; } = -1;
    public int MaxPlaytime { get; set; } = -1;
    public int MinAge { get; set; } = -1;
    public List<int> PlayerCounts { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string RulesUrl { get; set; } = string.Empty;
    public string OfficialUrl { get; set; } = string.Empty;
    public decimal AverageRating { get; set; } = -1;
    public decimal AverageLearningComplexity { get; set; } = -1;
    public decimal AverageStrategyComplexity { get; set; } = -1;
    public string Players => PlayerString();
    public string Playtime => PlaytimeString();

    private string PlayerString()
    {
        if(MinPlayers < 1 || MaxPlayers < 1 || MinPlayers > MaxPlayers)
        {
            return "Unknown Players";
        }

        if(MinPlayers == MaxPlayers)
        {
            return $"{MinPlayers} Player{(MinPlayers > 1 ? "s" : string.Empty)}";
        }

        return $"{MinPlayers}-{MaxPlayers} Players";
    }

    private string PlaytimeString()
    {
        if(MinPlaytime < 0 || MaxPlaytime < 0 || MinPlaytime > MaxPlaytime)
        {
            return "Unknown Minutes";
        }

        if(MinPlaytime == MaxPlaytime)
        {
            return $"{MinPlaytime} Minutes";
        }

        return $"{MinPlaytime}-{MaxPlaytime} Minutes";
    }
}
