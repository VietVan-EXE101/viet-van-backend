namespace VietVan.API.Entities;

public class Review
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int? LiteraryWorkId { get; set; }
    public LiteraryWork? LiteraryWork { get; set; }
    public int? DramaProductionId { get; set; }
    public DramaProduction? DramaProduction { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = "";
    public string Status { get; set; } = "Published";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

// Kept independently of the review so moderation history survives deletion.
public class ReviewModerationLog
{
    public long Id { get; set; }
    public long ReviewId { get; set; }
    public int AdminUserId { get; set; }
    public string OldStatus { get; set; } = "";
    public string NewStatus { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
