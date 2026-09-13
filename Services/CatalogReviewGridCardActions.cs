namespace QuiverLauncher.Services;

/// <summary>
/// Desktop catalog-review grid card chrome: at most three buttons on the card.
/// One to three actions stay inline. Four or more keep the first two inline and
/// put the rest in More (still three visible controls).
/// </summary>
public static class CatalogReviewGridCardActions
{
    public const int MaxChromeButtons = 3;
    public const int InlineWhenOverflow = 2;

    public enum Action
    {
        Add,
        Merge,
        Details,
        Hide,
        Unhide,
        Remove,
    }

    public enum ChromeKind
    {
        Add,
        Merge,
        Details,
        Hide,
        Unhide,
        Remove,
        More,
    }

    public sealed record ChromeItem(
        ChromeKind Kind,
        string IdentityKey,
        bool MenuAdd = false,
        bool MenuMerge = false,
        bool MenuDetails = false,
        bool MenuHide = false,
        bool MenuUnhide = false,
        bool MenuRemove = false)
    {
        public bool IsMore => Kind == ChromeKind.More;

        public string Label => Kind switch
        {
            ChromeKind.Add => "Add",
            ChromeKind.Merge => "Merge",
            ChromeKind.Details => "Details",
            ChromeKind.Hide => "Hide",
            ChromeKind.Unhide => "Unhide",
            ChromeKind.Remove => "Remove",
            ChromeKind.More => "More",
            _ => "",
        };

        public bool IsAddStyle => Kind == ChromeKind.Add;
        public bool IsMergeStyle => Kind == ChromeKind.Merge;
        public bool IsRemoveStyle => Kind == ChromeKind.Remove;
    }

    public readonly record struct Layout(
        IReadOnlyList<Action> Inline,
        IReadOnlyList<Action> Menu,
        IReadOnlyList<ChromeItem> Chrome)
    {
        public bool ShowMore => Menu.Count > 0;

        public int ColumnCount
        {
            get
            {
                var count = Chrome.Count;
                return count <= 0 ? 1 : count;
            }
        }

        public bool IsInline(Action action) => Inline.Contains(action);

        public bool IsMenu(Action action) => Menu.Contains(action);
    }

    public static Layout ForDesktop(
        bool canAdd,
        bool canMerge,
        bool showHide,
        bool showUnhide,
        bool showRemove,
        string identityKey = "")
    {
        var all = new List<Action>(6);
        if (canAdd)
            all.Add(Action.Add);
        if (canMerge)
            all.Add(Action.Merge);
        all.Add(Action.Details);
        if (showHide)
            all.Add(Action.Hide);
        if (showUnhide)
            all.Add(Action.Unhide);
        if (showRemove)
            all.Add(Action.Remove);

        IReadOnlyList<Action> inline;
        IReadOnlyList<Action> menu;
        if (all.Count <= MaxChromeButtons)
        {
            inline = all;
            menu = [];
        }
        else
        {
            inline = all.Take(InlineWhenOverflow).ToArray();
            menu = all.Skip(InlineWhenOverflow).ToArray();
        }

        var chrome = new List<ChromeItem>(MaxChromeButtons);
        foreach (var action in inline)
            chrome.Add(new ChromeItem(ToChrome(action), identityKey));
        if (menu.Count > 0)
        {
            chrome.Add(new ChromeItem(
                ChromeKind.More,
                identityKey,
                MenuAdd: menu.Contains(Action.Add),
                MenuMerge: menu.Contains(Action.Merge),
                MenuDetails: menu.Contains(Action.Details),
                MenuHide: menu.Contains(Action.Hide),
                MenuUnhide: menu.Contains(Action.Unhide),
                MenuRemove: menu.Contains(Action.Remove)));
        }

        return new Layout(inline, menu, chrome);
    }

    internal static ChromeKind ToChrome(Action action) => action switch
    {
        Action.Add => ChromeKind.Add,
        Action.Merge => ChromeKind.Merge,
        Action.Details => ChromeKind.Details,
        Action.Hide => ChromeKind.Hide,
        Action.Unhide => ChromeKind.Unhide,
        Action.Remove => ChromeKind.Remove,
        _ => ChromeKind.Details,
    };
}
