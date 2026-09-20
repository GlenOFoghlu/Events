using DublinEventsCollector.Models;

namespace DublinEventsCollector.Collectors;

public abstract class CandidateListingSource : IEventSource
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public bool RequiresConfiguration => true;

    public virtual Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        return Task.FromResult(EventSourceResult.Empty(
            Name,
            "Candidate listing source. Add a source-specific collector after checking terms, robots rules, and page structure."));
    }
}

public sealed class DublinIeCandidateSource : CandidateListingSource
{
    public override string Name => "dublin-ie";
    public override string Description => "Dublin.ie curated What's On listings.";
}

public sealed class EntertainmentIeCandidateSource : CandidateListingSource
{
    public override string Name => "entertainment-ie";
    public override string Description => "Entertainment.ie Dublin listings.";
}

public sealed class WhatsOnDublinCandidateSource : CandidateListingSource
{
    public override string Name => "whats-on-dublin";
    public override string Description => "What's On Dublin gig-focused listings.";
}

public sealed class WhelansCandidateSource : CandidateListingSource
{
    public override string Name => "whelans";
    public override string Description => "Whelan's Dublin gig listings.";
}

public sealed class TheAcademyCandidateSource : CandidateListingSource
{
    public override string Name => "the-academy";
    public override string Description => "The Academy Dublin listings.";
}

public sealed class TheGrandSocialCandidateSource : CandidateListingSource
{
    public override string Name => "the-grand-social";
    public override string Description => "The Grand Social listings.";
}

public sealed class WorkmansClubCandidateSource : CandidateListingSource
{
    public override string Name => "workmans-club";
    public override string Description => "The Workman's Club listings.";
}
