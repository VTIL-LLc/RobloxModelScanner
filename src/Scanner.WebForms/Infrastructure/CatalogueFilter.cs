using System;
using System.Collections.Generic;
using System.Globalization;

namespace Scanner.WebForms.Infrastructure
{
    /// <summary>
    /// What the catalogue page is currently asking the API for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The page is a form. Everything here comes out of the query string of the page's own address, which
    /// means the filter an operator has applied is the address they are looking at -- it survives a refresh,
    /// it can be sent to somebody else, and the back button does what it says. Nothing is held in the session,
    /// so two tabs can hold two different filters without one of them quietly changing the other.
    /// </para>
    /// <para>
    /// The values are not trusted and are not normalised here beyond what the address itself needs. The API
    /// validates and clamps everything it receives, and a second set of limits on this side would be a second
    /// answer to what a legal page size is.
    /// </para>
    /// </remarks>
    public sealed class CatalogueFilter
    {
        /// <summary>
        /// The page size the catalogue is read at.
        /// </summary>
        /// <remarks>
        /// Fifty rows is the API's own default and about a screen and a half of scroll on a normal monitor. It
        /// is also the largest page a person reads rather than skims, which is the point: a catalogue of a
        /// hundred thousand models is searched, not browsed.
        /// </remarks>
        public const int DefaultPageSize = 50;

        /// <summary>
        /// The largest page size the site will ask for.
        /// </summary>
        /// <remarks>
        /// The API caps this at the same number, so this is not a second opinion about the ceiling: it is the
        /// site stating the same one, which is what keeps a hand-edited address from asking for a page the API
        /// would answer with a different size than the pager then divides by. Five hundred is the largest page
        /// the reader may ask for; the rows menu offers nothing beyond it.
        /// </remarks>
        public const int MaximumPageSize = 500;

        /// <summary>
        /// The order the catalogue is shown in when the address names none.
        /// </summary>
        /// <remarks>
        /// The row the sweep discovered most recently comes first, because that is the question a visitor arrives
        /// with: what has the scanner added lately. It is a key into <see cref="Orders"/> rather than a column name
        /// so that the menu, the address and the default cannot name three different orders.
        /// </remarks>
        public const string DefaultOrderKey = "recent";

        /// <summary>
        /// The orders the catalogue may be shown in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// One list, read by the menu that offers an order, by the reader that resolves the address back into a
        /// sort field, and by the default. Adding an order is one entry here; a menu built separately from the
        /// resolver would be a menu offering keys nothing answers.
        /// </para>
        /// <para>
        /// The keys are short because they are written into addresses a person reads and sends on. The labels are
        /// sentences because a menu is read rather than parsed.
        /// </para>
        /// </remarks>
        public static readonly IReadOnlyList<OrderChoice> Orders = new OrderChoice[]
        {
            new OrderChoice("recent", "Recently added", "Found", true),
            new OrderChoice("earliest", "Earliest added first", "Found", false),
            new OrderChoice("newest", "Newest asset first", "Model", true),
            new OrderChoice("score", "Highest score first", "Score", true),
            new OrderChoice("name", "Name, A to Z", "Name", false),
            new OrderChoice("creator", "Creator, A to Z", "CreatorName", false),
        };


        /// <summary>
        /// Gets or sets the free-text term the catalogue is searched for.
        /// </summary>
        public string Search { get; set; }

        /// <summary>
        /// Gets or sets whether results are restricted to assets with or without script data, or
        /// <see langword="null"/> for either.
        /// </summary>
        public bool? HasScripts { get; set; }

        /// <summary>
        /// Gets or sets whether results are restricted to assets with or without detections, or
        /// <see langword="null"/> for either.
        /// </summary>
        public bool? HasDetections { get; set; }

        /// <summary>
        /// Gets or sets the rule whose matches the results are restricted to, or an empty string for all of
        /// them.
        /// </summary>
        public string RuleTag { get; set; }

        /// <summary>
        /// Gets or sets the one-based page number.
        /// </summary>
        public int Page { get; set; }

        /// <summary>
        /// Gets or sets the number of models per page.
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Gets or sets the catalogue column the results are ordered by.
        /// </summary>
        public string SortBy { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the order runs from highest to lowest.
        /// </summary>
        public bool Descending { get; set; }

        /// <summary>
        /// Gets the key of the order currently in force.
        /// </summary>
        /// <remarks>
        /// The menu marks the option whose key this is, and an address names an order by it. A sort field and
        /// direction that no entry of <see cref="Orders"/> describes falls back to the default rather than
        /// producing an address with no option selected, which would be a menu that shows the wrong order.
        /// </remarks>
        public string OrderKey
        {
            get
            {
                foreach (OrderChoice choice in Orders)
                {
                    if (string.Equals(choice.SortBy, SortBy, StringComparison.OrdinalIgnoreCase)
                        && choice.Descending == Descending)
                    {
                        return choice.Key;
                    }
                }

                return DefaultOrderKey;
            }
        }


        /// <summary>
        /// The catalogue columns a caller may order by, as the API names them.
        /// </summary>
        /// <remarks>
        /// A closed list rather than "whatever the address said". The value ends up in the API's own sort
        /// parameter, and the API resolves it by name; a value it does not recognise falls back to its own
        /// default, which is the order the catalogue found the rows in. Keeping the list here as well means a
        /// typo in an address produces that default rather than a request that names a column nothing reads.
        /// </remarks>
        private static readonly string[] Sortable =
        {
            "Found", "Model", "Date", "Name", "CreatorName", "CreatorId", "HasScripts", "Score", "Tags",
            "Description",
        };

        /// <summary>
        /// Reads the filter out of an address.
        /// </summary>
        /// <param name="query">The query string.</param>
        /// <returns>The filter.</returns>
        /// <remarks>
        /// <para>
        /// One reader, used by the page and by the fragment handler that refreshes one of its regions. The
        /// region's address carries the filter the page was rendered with, and both ends have to arrive at the
        /// same filter from it or the refresh would replace the table with a differently filtered one.
        /// </para>
        /// <para>
        /// The parameter names here are the page's own and are deliberately not the API's. An address is
        /// something a person reads, sends to somebody else and edits; the API's names describe a call.
        /// </para>
        /// </remarks>
        public static CatalogueFilter FromQuery(System.Collections.Specialized.NameValueCollection query)
        {
            CatalogueFilter filter = new CatalogueFilter
            {
                Page = 1,
                PageSize = DefaultPageSize,
                // The default order is the order the catalogue found the rows in, most recent first. A visitor
                // arriving at the catalogue without a filter wants what the scanner has just discovered, which
                // is not the same list as the highest asset identifiers: those are what Roblox published most
                // recently, and this sweep has been running for years.
                SortBy = "Found",
                Descending = true,
            };

            if (query == null) return filter;

            filter.Search = Trim(query["q"], 128);
            filter.RuleTag = Trim(query["tag"], 160);
            filter.HasScripts = Flag(query["scripts"]);
            filter.HasDetections = Flag(query["detections"]);
            filter.Page = Whole(query["page"], 1, 1, 1_000_000);
            filter.PageSize = Whole(query["size"], DefaultPageSize, 1, MaximumPageSize);

            // The menu's key first, and the older pair of parameters only when no key is named. Both reach the
            // same two fields, and the key wins because it is the one the page itself writes: a link sent before
            // the menu existed still carries the pair, and still lands on the order it asked for.
            OrderChoice order = FindOrder(Trim(query["order"], 16));

            if (order != null)
            {
                filter.SortBy = order.SortBy;
                filter.Descending = order.Descending;
            }
            else
            {
                filter.Descending = !string.Equals(Trim(query["dir"], 8), "asc", StringComparison.OrdinalIgnoreCase);

                string sort = Trim(query["sort"], 32);

                if (sort.Length > 0 && IsSortable(sort)) filter.SortBy = sort;
            }

            return filter;
        }

        /// <summary>
        /// Reads a bounded text value.
        /// </summary>
        /// <param name="raw">The raw value.</param>
        /// <param name="maximumLength">The longest value accepted.</param>
        /// <returns>The value, or an empty string.</returns>
        /// <remarks>
        /// Truncated rather than refused. The value is a search term; a term that was too long to be a real one
        /// is answered by searching for its beginning, which is what the person meant, instead of by an error
        /// page about the length of an address.
        /// </remarks>
        private static string Trim(string raw, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            string value = raw.Trim();

            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }

        /// <summary>
        /// Reads a three-state flag out of its form value.
        /// </summary>
        /// <param name="raw">The raw value.</param>
        /// <returns>The flag, or <see langword="null"/> when the address expressed no preference.</returns>
        private static bool? Flag(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            if (raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (raw == "0" || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) return false;

            return null;
        }

        /// <summary>
        /// Reads a whole number out of an address, clamped.
        /// </summary>
        /// <param name="raw">The raw value.</param>
        /// <param name="fallback">The value used when the address carried nothing usable.</param>
        /// <param name="minimum">The smallest accepted value.</param>
        /// <param name="maximum">The largest accepted value.</param>
        /// <returns>The value.</returns>
        private static int Whole(string raw, int fallback, int minimum, int maximum)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;

            int parsed;

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return fallback;

            return parsed < minimum ? minimum : parsed > maximum ? maximum : parsed;
        }

        /// <summary>
        /// Decides whether a name is one of the columns the API can order by.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>Whether it may be used.</returns>
        private static bool IsSortable(string name)
        {
            foreach (string candidate in Sortable)
            {
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves an order key back into the entry that carries it.
        /// </summary>
        /// <param name="key">The key out of an address.</param>
        /// <returns>The entry, or <see langword="null"/> when no entry carries that key.</returns>
        private static OrderChoice FindOrder(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            foreach (OrderChoice choice in Orders)
            {
                if (string.Equals(choice.Key, key, StringComparison.OrdinalIgnoreCase)) return choice;
            }

            return null;
        }

        /// <summary>
        /// One entry of the catalogue's order menu.
        /// </summary>
        /// <remarks>
        /// The key is what an address carries, the label is what the menu shows, and the sort field and direction
        /// are what the API is asked for. Keeping all four together is what makes a new order one entry here
        /// rather than an entry in the menu plus a branch in the reader plus a line in the address builder.
        /// </remarks>
        public sealed class OrderChoice
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="OrderChoice"/> class.
            /// </summary>
            /// <param name="key">The short name the address carries.</param>
            /// <param name="label">The text the menu shows.</param>
            /// <param name="sortBy">The catalogue column the API orders by.</param>
            /// <param name="descending">Whether the order runs from highest to lowest.</param>
            public OrderChoice(string key, string label, string sortBy, bool descending)
            {
                Key = key;
                Label = label;
                SortBy = sortBy;
                Descending = descending;
            }

            /// <summary>
            /// Gets the short name the address carries.
            /// </summary>
            public string Key { get; }

            /// <summary>
            /// Gets the text the menu shows.
            /// </summary>
            public string Label { get; }

            /// <summary>
            /// Gets the catalogue column the API orders by.
            /// </summary>
            public string SortBy { get; }

            /// <summary>
            /// Gets a value indicating whether the order runs from highest to lowest.
            /// </summary>
            public bool Descending { get; }
        }

        /// <summary>
        /// Builds the query the API is asked with.
        /// </summary>
        /// <returns>The pairs, encoded by the client rather than here.</returns>
        /// <remarks>
        /// Absent filters are left out entirely rather than sent empty. The API reads an absent parameter and an
        /// empty one differently for the two flags -- absent means "either", and a flag has no empty value --
        /// so sending every parameter always would silently filter the catalogue down to nothing.
        /// </remarks>
        public KeyValuePair<string, string>[] ToQuery()
        {
            List<KeyValuePair<string, string>> query = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("page", Page.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("pageSize", PageSize.ToString(CultureInfo.InvariantCulture)),
            };

            if (!string.IsNullOrWhiteSpace(Search))
            {
                query.Add(new KeyValuePair<string, string>("search", Search.Trim()));
            }

            if (HasScripts.HasValue)
            {
                query.Add(new KeyValuePair<string, string>("hasScripts", HasScripts.Value ? "true" : "false"));
            }

            if (HasDetections.HasValue)
            {
                query.Add(new KeyValuePair<string, string>("hasObfuscation", HasDetections.Value ? "true" : "false"));
            }

            if (!string.IsNullOrWhiteSpace(RuleTag))
            {
                query.Add(new KeyValuePair<string, string>("ruleTag", RuleTag.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(SortBy))
            {
                query.Add(new KeyValuePair<string, string>("sortBy", SortBy.Trim()));
                query.Add(new KeyValuePair<string, string>("sortDirection", Descending ? "desc" : "asc"));
            }

            return query.ToArray();
        }

        /// <summary>
        /// Builds the part of a catalogue address that carries the filter.
        /// </summary>
        /// <param name="page">The page number to name, which is not necessarily the one being shown.</param>
        /// <returns>The pairs, as name and value, ready for a link or a region's address.</returns>
        /// <remarks>
        /// <para>
        /// Separate from <see cref="ToQuery"/> because this is what a page's own links carry, and a link that
        /// named the API's parameters would be a link that describes a call rather than a page. The two are kept
        /// side by side so that adding a filter is one edit in one file rather than two that can disagree.
        /// </para>
        /// <para>
        /// The page number is a parameter rather than read off the instance so that the pager can name the page
        /// it is linking to with the filter it is linking from. A pager that built its own pairs would be the
        /// second implementation of this method that the remark above is about.
        /// </para>
        /// </remarks>
        public KeyValuePair<string, string>[] ToAddress(int page)
        {
            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>();

            if (!string.IsNullOrWhiteSpace(Search)) pairs.Add(new KeyValuePair<string, string>("q", Search.Trim()));
            if (HasScripts.HasValue) pairs.Add(new KeyValuePair<string, string>("scripts", HasScripts.Value ? "1" : "0"));
            if (HasDetections.HasValue) pairs.Add(new KeyValuePair<string, string>("detections", HasDetections.Value ? "1" : "0"));
            if (!string.IsNullOrWhiteSpace(RuleTag)) pairs.Add(new KeyValuePair<string, string>("tag", RuleTag.Trim()));
            if (OrderKey != DefaultOrderKey) pairs.Add(new KeyValuePair<string, string>("order", OrderKey));
            if (PageSize != DefaultPageSize)
            {
                pairs.Add(new KeyValuePair<string, string>("size", PageSize.ToString(CultureInfo.InvariantCulture)));
            }

            if (page > 1) pairs.Add(new KeyValuePair<string, string>("page", page.ToString(CultureInfo.InvariantCulture)));

            return pairs.ToArray();
        }

        /// <summary>
        /// Gets a value indicating whether anything at all is being filtered.
        /// </summary>
        /// <remarks>
        /// What a clear control is offered against. A clear button that is offered when nothing is filtered is a
        /// button that does nothing, and a page with one of those is a page whose reader stops trusting the
        /// others.
        /// </remarks>
        public bool IsFiltered
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Search)
                       || HasScripts.HasValue
                       || HasDetections.HasValue
                       || !string.IsNullOrWhiteSpace(RuleTag);
            }
        }
    }
}
