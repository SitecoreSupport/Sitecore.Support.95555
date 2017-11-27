namespace Sitecore.Support.ContentSearch.Pipelines.GetContextIndex
{
  using System.Collections.Generic;
  using System.Linq;
  using Sitecore.Caching.Generics;
  using Sitecore.ContentSearch.Abstractions;
  using Sitecore.Data.Items;
  using Sitecore.Diagnostics;
  using Sitecore.Reflection;
  using Sitecore.SecurityModel;
  using Sitecore.ContentSearch.Pipelines.GetContextIndex;
  using Sitecore.ContentSearch;

  /// <summary>
  /// The <see cref="FetchIndex"/> class.
  /// </summary>
  public class FetchIndex : GetContextIndexProcessor
  {
    private readonly ISettings settings;

    public FetchIndex()
    {
      this.settings = ContentSearchManager.Locator.GetInstance<ISettings>();
    }

    internal FetchIndex(ISettings settings)
    {
      this.settings = settings;
    }

    /// <summary>Processes the specified args.</summary>
    /// <param name="args">The args.</param>
    public override void Process(GetContextIndexArgs args)
    {
      if (args == null)
      {
        return;
      }

      if (args.Result != null)
      {
        return;
      }

      args.Result = this.GetContextIndex(args.Indexable, args);
    }

    /// <summary>Gets the index of the context.</summary>
    /// <param name="indexable">The indexable.</param>
    /// <param name="args">The args.</param>
    /// <returns>The result.</returns>
    protected virtual string GetContextIndex(IIndexable indexable, GetContextIndexArgs args)
    {
      if (indexable == null)
      {
        return null;
      }

      List<ISearchIndex> includedIndexes = new List<ISearchIndex>();
      AbstractSearchIndex tempIndex;

      foreach (var searchIndex in ContentSearchManager.Indexes)
      {
        tempIndex = searchIndex as AbstractSearchIndex;

        if ((tempIndex == null) &&
            searchIndex.Crawlers.Any(c => !c.IsExcludedFromIndex(indexable)))
        {
          includedIndexes.Add(searchIndex);
        }
      }

      var indexes = includedIndexes.AsEnumerable();

      if (!indexes.Any())
      {
        indexes = this.FindIndexesRelatedToIndexable(args.Indexable, ContentSearchManager.Indexes);
      }

      // ReSharper disable once PossibleMultipleEnumeration
      var rankedIndexes = RankContextIndexes(indexes, indexable);

      var searchIndices = rankedIndexes as Tuple<ISearchIndex, int>[] ?? rankedIndexes.ToArray();

      // No crawlers index this item..
      if (!searchIndices.Any())
      {
        Log.Error(string.Format("There is no appropriate index for {0} - {1}. You have to add an index crawler that will cover this item", indexable.AbsolutePath, indexable.Id), this);
        return null;
      }

      // If only one matches then use that..
      if (searchIndices.Count() == 1)
      {
        return searchIndices.First().First.Name;
      }

      if (searchIndices.First().Second < searchIndices.Skip(1).First().Second)
      {
        return searchIndices.First().First.Name;
      }

      // Get default type from setting ..
      var defaultTypeString = this.settings.GetSetting("ContentSearch.DefaultIndexType", "");
      var defaultType = ReflectionUtil.GetTypeInfo(defaultTypeString);

      //If we cant evaluate this type then return first ..
      if (defaultType == null)
      {
        return searchIndices[0].First.Name;
      }

      // Return the one that matches the default type ..
      var matchedIndex = searchIndices.Where(i => i.First.GetType() == defaultType).OrderBy(i => i.First.Name).ToArray();
      return matchedIndex.Any() ? matchedIndex[0].First.Name : searchIndices[0].First.Name;
    }

    protected virtual IEnumerable<ISearchIndex> FindIndexesRelatedToIndexable(IIndexable indexable, IEnumerable<ISearchIndex> indexes)
    {
      SitecoreIndexableItem sitecoreIndexableItem = indexable as SitecoreIndexableItem;

      if (sitecoreIndexableItem == null)
      {
        return new List<ISearchIndex>();
      }

      Item item = sitecoreIndexableItem.Item;

      using (new SecurityDisabler())
      {
        using (new WriteCachesDisabler())
        {
          return indexes
              .Where(i => i.Crawlers.OfType<SitecoreItemCrawler>()
                  .Where(crawler => crawler.RootItem != null)
                  .Any(crawler => item.Database.Name.Equals(crawler.Database, System.StringComparison.InvariantCultureIgnoreCase)
                                  && item.Paths.LongID.StartsWith(crawler.RootItem.Paths.LongID, System.StringComparison.InvariantCulture)));
        }
      }
    }

    /// <summary>Ranks the context indexes.</summary>
    /// <param name="indexes">The indexes.</param>
    /// <param name="indexable">The indexable.</param>
    /// <returns>Ranked indexes.</returns>
    protected virtual IEnumerable<Tuple<ISearchIndex, int>> RankContextIndexes(IEnumerable<ISearchIndex> indexes, IIndexable indexable)
    {
      return indexes.Distinct().Select(i => Tuple.New(i, (i is IContextIndexRankable) ? ((IContextIndexRankable)i).GetContextIndexRanking(indexable) : int.MaxValue)).OrderBy(i => i.Second);
    }
  }
}