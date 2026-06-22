namespace Mangrove.Server.Data;

// Phase 3 "organize + users" entities (spec §6, §14): collections, reading lists, want-to-read,
// ratings/reviews, bookmarks, and per-user age restrictions.

/// <summary>One per user; limits visible series by age rating tier (spec §7).</summary>
public class AgeRestriction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    /// <summary>Max allowed <see cref="AgeRatingMap"/> tier (0 = no restriction / not set).</summary>
    public int MaxAgeRating { get; set; }
    public bool IncludeUnknowns { get; set; } = true;
}

public class Collection
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<CollectionItem> Items { get; set; } = new();
}

public class CollectionItem
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public Collection Collection { get; set; } = null!;
    public int SeriesId { get; set; }
    public Series Series { get; set; } = null!;
    public int Order { get; set; }
}

public class ReadingList
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ReadingListItem> Items { get; set; } = new();
}

public class ReadingListItem
{
    public int Id { get; set; }
    public int ReadingListId { get; set; }
    public ReadingList ReadingList { get; set; } = null!;
    public int ChapterId { get; set; }
    public Chapter Chapter { get; set; } = null!;
    public int Order { get; set; }
}

public class WantToRead
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int SeriesId { get; set; }
    public Series Series { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A user's star rating (1-5) and optional review body for a series.</summary>
public class SeriesReview
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int SeriesId { get; set; }
    public Series Series { get; set; } = null!;
    public int Stars { get; set; }
    public string? Body { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Bookmark
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int ChapterId { get; set; }
    public Chapter Chapter { get; set; } = null!;
    public int PageNum { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Normalizes the free-text ComicInfo/EPUB age rating into ordered tiers so it can be filtered in
/// SQL. Higher tier = more mature. Tier 0 means unknown/unmapped (spec §7 age restrictions).
/// </summary>
public static class AgeRatingMap
{
    // tier -> friendly label for the highest representative rating
    public static readonly IReadOnlyList<string> TierLabels = new[]
    {
        "Unknown", "Everyone", "Everyone 10+", "Teen", "Mature 17+", "Adults Only 18+",
    };

    public static int Tier(string? rating)
    {
        if (string.IsNullOrWhiteSpace(rating)) return 0;
        var r = rating.Trim().ToLowerInvariant();
        return r switch
        {
            "everyone" or "g" or "all ages" or "all-ages" or "early childhood" or "kids to adults"
                or "rating pending" => 1,
            "everyone 10+" or "10+" or "pg" or "everyone 10plus" => 2,
            "teen" or "t" or "teen 13+" or "pg-13" or "ma15+" => 3,
            "mature 17+" or "m" or "mature" or "r" or "r-rated" or "17+" => 4,
            "adults only 18+" or "adults only" or "ao" or "x18+" or "r18+" or "18+" or "explicit" => 5,
            _ => 0,
        };
    }
}
