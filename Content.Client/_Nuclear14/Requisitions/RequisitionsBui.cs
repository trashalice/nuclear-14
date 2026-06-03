using System.Linq;
using System.Numerics;
using Content.Shared._Nuclear14.Requisitions;
using Content.Shared._Nuclear14.Requisitions.Components;
using Content.Shared.Storage.Components;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.Shared._Nuclear14.Requisitions.Components.RequisitionsElevatorMode;

namespace Content.Client._Nuclear14.Requisitions;

[UsedImplicitly]
public sealed class RequisitionsBui : BoundUserInterface
{
    private const float CategoryMinWidth = 180f;
    private const float CategoryPanelPadding = 12f;

    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IResourceCache _resources = default!;

    private readonly SpriteSystem _sprite;
    private readonly Dictionary<CartKey, int> _cart = new();
    private readonly Dictionary<CartKey, RequisitionsProductCard> _productCards = new();

    private RequisitionsComputerComponent? _state;
    private RequisitionsWindow? _window;
    private int? _selectedCategory;

    public RequisitionsBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _sprite = EntMan.System<SpriteSystem>();
    }

    protected override void Open()
    {
        base.Open();
        Refresh();
    }

    private void EnsureWindow()
    {
        if (_window != null)
            return;

        _window = this.CreateWindow<RequisitionsWindow>();

        _window.SearchBar.OnTextChanged += _ => RefreshVisibleEntries();
        _window.PlatformButton.OnPressed += _ => PressPlatformButton();
        _window.ClearCartButton.OnPressed += _ =>
        {
            _cart.Clear();
            RefreshVisibleEntries();
        };
        _window.BuyButton.OnPressed += _ => BuyCart();
        _window.SellButton.OnPressed += _ =>
        {
            if (_state is { PlatformLowered: Raised, Busy: false })
                SendMessage(new RequisitionsPlatformMsg(false));
        };
    }

    public void Refresh()
    {
        EnsureWindow();
        RefreshShop();

        if (_window is { IsOpen: false })
            _window.OpenCentered();
    }

    private void RefreshShop()
    {
        _entities.TryGetComponent(Owner, out _state);

        if (_state is { } computer &&
            _selectedCategory is { } selectedCategory &&
            selectedCategory >= computer.Categories.Count)
        {
            _selectedCategory = null;
        }

        UpdateLinkStatus();
        UpdatePlatformButtons();
        UpdateBalance();
        UpdateSellTab();
        _window?.SetPlatformBusy(_state?.BusyStart, _state?.BusyEnd);
        PopulateCategories();
        PopulateProducts();
        RefreshCart();
        PopulatePendingOrders();
        UpdateHistory();
        UpdateBounties();
    }

    private void UpdateBounties()
    {
        if (_window == null)
            return;

        _window.BountiesContainer.DisposeAllChildren();

        var bounties = _state?.Bounties;
        if (bounties == null || bounties.Count == 0)
        {
            _window.BountiesStatusLabel.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("n14-requisitions-bounties-empty")));
            return;
        }

        _window.BountiesStatusLabel.SetMessage(FormattedMessage.FromUnformatted(string.Empty));
        var completed = _state?.CompletedBounties ?? new List<string>();
        foreach (var bounty in bounties)
        {
            var name = bounty.Name is { } loc && Loc.TryGetString(loc, out var localized)
                ? localized
                : _prototypes.TryIndex<EntityPrototype>(bounty.Item, out var itemProto)
                    ? itemProto.Name
                    : bounty.Item.Id;

            var done = completed.Contains(bounty.Id);
            var row = new RichTextLabel { HorizontalExpand = true };
            row.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString(
                done ? "n14-requisitions-bounty-row-done" : "n14-requisitions-bounty-row",
                ("amount", bounty.Amount),
                ("item", name),
                ("reward", bounty.Reward))));
            _window.BountiesContainer.AddChild(row);
        }
    }

    private void UpdateHistory()
    {
        if (_window == null)
            return;

        _window.HistoryContainer.DisposeAllChildren();

        var history = _state?.History;
        if (history == null || history.Count == 0)
        {
            _window.HistoryStatusLabel.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("n14-requisitions-history-empty")));
            return;
        }

        _window.HistoryStatusLabel.SetMessage(FormattedMessage.FromUnformatted(string.Empty));
        foreach (var entry in history)
        {
            var name = _prototypes.TryIndex<EntityPrototype>(entry.Crate, out var proto) ? proto.Name : entry.Crate;
            var row = new RichTextLabel { HorizontalExpand = true };
            row.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString(
                "n14-requisitions-history-row",
                ("buyer", entry.Buyer),
                ("amount", entry.Amount),
                ("item", name),
                ("cost", entry.Cost))));
            _window.HistoryContainer.AddChild(row);
        }
    }

    private void UpdateSellTab()
    {
        if (_window == null)
            return;

        var value = _state?.PlatformSaleValue ?? 0;
        var count = _state?.PlatformSaleCount ?? 0;

        _window.SellInfoLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString(
            "n14-requisitions-sell-info", ("count", count), ("value", value))));

        var canSell = _state is { PlatformLowered: Raised, Busy: false };
        _window.SellButton.Disabled = !canSell;

        var status = string.Empty;
        if (_state is not { Linked: true })
            status = Loc.GetString("n14-requisitions-sell-no-platform");
        else if (_state.PlatformLowered != Raised)
            status = Loc.GetString("n14-requisitions-sell-raise-first");
        else if (count == 0)
            status = Loc.GetString("n14-requisitions-sell-empty");

        _window.SellStatusLabel.SetMessage(FormattedMessage.FromUnformatted(status));

        _window.SellCatalogContainer.DisposeAllChildren();
        var sellEntries = _state?.SellEntries;
        if (sellEntries == null || sellEntries.Count == 0)
        {
            _window.SellCatalogContainer.AddChild(new Label
            {
                Text = Loc.GetString("n14-requisitions-sell-catalog-empty"),
            });
            return;
        }

        foreach (var entry in sellEntries)
        {
            var name = entry.Name is { } loc && Loc.TryGetString(loc, out var localized)
                ? localized
                : _prototypes.TryIndex<EntityPrototype>(entry.Item, out var itemProto)
                    ? itemProto.Name
                    : entry.Item.Id;

            string reward;
            if (entry.Exchange.Count > 0)
            {
                var items = new List<string>();
                foreach (var ex in entry.Exchange)
                {
                    items.Add(_prototypes.TryIndex<EntityPrototype>(ex, out var exProto) ? exProto.Name : ex.Id);
                }

                reward = string.Join(", ", items);
                if (entry.Value > 0)
                    reward = $"${entry.Value} + {reward}";
            }
            else
            {
                reward = $"${entry.Value}";
            }

            var row = new RichTextLabel { HorizontalExpand = true };
            row.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString(
                "n14-requisitions-sell-catalog-row", ("item", name), ("reward", reward))));
            _window.SellCatalogContainer.AddChild(row);
        }
    }

    private void RefreshVisibleEntries()
    {
        PopulateProducts();
        RefreshCart();
        PopulatePendingOrders();
    }

    private void UpdateLinkStatus()
    {
        if (_window == null)
            return;

        var linked = _state is { Linked: true };
        var key = linked ? "n14-requisitions-linked" : "n14-requisitions-unlinked";
        _window.LinkStatusLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString(key)));
    }

    private void UpdatePlatformButtons()
    {
        if (_window == null)
            return;

        _window.PlatformButton.Disabled = true;

        if (_state == null)
        {
            _window.PlatformButton.Text = Loc.GetString("n14-requisitions-platform-missing");
            return;
        }

        if (_state.Busy || _state.PlatformLowered is Preparing or Lowering or Raising)
        {
            _window.PlatformButton.Text = Loc.GetString("n14-requisitions-platform-busy");
            return;
        }

        switch (_state.PlatformLowered)
        {
            case Lowered:
                _window.PlatformButton.Text = Loc.GetString("n14-requisitions-platform-raise");
                _window.PlatformButton.Disabled = false;
                break;
            case Raised:
                _window.PlatformButton.Text = Loc.GetString("n14-requisitions-platform-lower");
                _window.PlatformButton.Disabled = false;
                break;
            default:
                _window.PlatformButton.Text = Loc.GetString("n14-requisitions-platform-missing");
                break;
        }
    }

    private void PressPlatformButton()
    {
        if (_state == null || _state.Busy)
            return;

        switch (_state.PlatformLowered)
        {
            case Lowered:
                SendMessage(new RequisitionsPlatformMsg(true));
                break;
            case Raised:
                SendMessage(new RequisitionsPlatformMsg(false));
                break;
        }
    }

    private void UpdateBalance()
    {
        if (_window == null || _state == null)
            return;

        _window.BudgetLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString(
            "n14-requisitions-balance",
            ("balance", _state.Balance))));

        _window.CapacityLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString(
            "n14-requisitions-capacity",
            ("count", _state.OrderCount),
            ("capacity", _state.Capacity))));
    }

    private void PopulateCategories()
    {
        if (_window == null)
            return;

        _window.CategoriesContainer.DisposeAllChildren();

        var buttons = new List<Button>();

        var allButton = CreateCategoryButton(Loc.GetString("n14-requisitions-category-all"), _selectedCategory == null);
        allButton.OnPressed += _ =>
        {
            _selectedCategory = null;
            RefreshShop();
        };
        buttons.Add(allButton);

        if (_state is { } computer)
        {
            for (var i = 0; i < computer.Categories.Count; i++)
            {
                var categoryIndex = i;
                var button = CreateCategoryButton(Loc.GetString(computer.Categories[i].Name), _selectedCategory == categoryIndex);
                button.OnPressed += _ =>
                {
                    _selectedCategory = categoryIndex;
                    RefreshShop();
                };
                buttons.Add(button);
            }
        }

        var buttonWidth = CategoryMinWidth;
        foreach (var button in buttons)
        {
            button.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
            buttonWidth = Math.Max(buttonWidth, MathF.Ceiling(button.DesiredSize.X));
        }

        _window.CategoryPanel.MinWidth = buttonWidth + CategoryPanelPadding;
        foreach (var button in buttons)
        {
            button.MinWidth = buttonWidth;
            _window.CategoriesContainer.AddChild(button);
        }
    }

    private static Button CreateCategoryButton(string text, bool disabled)
    {
        return new Button
        {
            Text = text,
            Disabled = disabled,
            ClipText = false,
            HorizontalExpand = true,
            StyleClasses = { "ButtonSquare" },
        };
    }

    private void PopulateProducts()
    {
        if (_window == null)
            return;

        _window.ProductsContainer.DisposeAllChildren();
        _productCards.Clear();

        if (_state is not { } computer)
            return;

        var search = _window.SearchBar.Text.Trim();
        var added = 0;

        for (var categoryIndex = 0; categoryIndex < computer.Categories.Count; categoryIndex++)
        {
            if (_selectedCategory != null && _selectedCategory != categoryIndex)
                continue;

            var category = computer.Categories[categoryIndex];
            for (var entryIndex = 0; entryIndex < category.Entries.Count; entryIndex++)
            {
                var entry = category.Entries[entryIndex];
                var display = GetDisplay(entry);
                if (!MatchesSearch(display, search))
                    continue;

                var key = new CartKey(categoryIndex, entryIndex);
                var costText = GetCostText(entry);
                if (entry.Stock >= 0)
                {
                    var bought = _state?.Purchased.GetValueOrDefault(entry.Crate.Id) ?? 0;
                    costText += "  " + Loc.GetString("n14-requisitions-stock-left", ("left", Math.Max(0, entry.Stock - bought)));
                }

                var card = new RequisitionsProductCard
                {
                    CategoryIndex = categoryIndex,
                    EntryIndex = entryIndex,
                    UnitCost = entry.Cost,
                    Cost = { Text = costText },
                    Icon =
                    {
                        Textures = display.IconTextures,
                        Modulate = display.IconModulate,
                    },
                };
                card.ProductName.SetMessage(FormattedMessage.FromUnformatted(display.Name), defaultColor: RequisitionsUiStyles.Green);
                card.Description.SetMessage(FormattedMessage.FromUnformatted(display.Description), defaultColor: RequisitionsUiStyles.TextDim);
                card.ToolTip = GetCrateContentsText(entry);

                card.AddButton.OnPressed += _ => AddToCart(key);
                card.RemoveButton.OnPressed += _ => RemoveFromCart(key);
                card.Add10Button.OnPressed += _ => AddToCart(key, 10);
                card.MaxButton.OnPressed += _ => AddToCart(key, int.MaxValue);

                _productCards[key] = card;
                UpdateProductCard(card, key);
                _window.ProductsContainer.AddChild(card);
                added++;
            }
        }

        if (added == 0)
        {
            _window.ProductsContainer.AddChild(new Label
            {
                Text = Loc.GetString("n14-requisitions-products-empty"),
            });
        }
    }

    private void AddToCart(CartKey key, int amount = 1)
    {
        if (!TryGetEntry(key, out var entry))
            return;

        var add = Math.Min(amount, MaxAddable(key, entry));
        if (add <= 0)
            return;

        _cart[key] = GetCartAmount(key) + add;
        RefreshCart();
        UpdateVisibleProductCards();
    }

    private void RemoveFromCart(CartKey key)
    {
        var amount = GetCartAmount(key);
        if (amount <= 0)
            return;

        if (amount == 1)
            _cart.Remove(key);
        else
            _cart[key] = amount - 1;

        RefreshCart();
        UpdateVisibleProductCards();
    }

    private bool CanAdd(CartKey key, RequisitionsEntry entry)
    {
        return MaxAddable(key, entry) > 0;
    }

    private int MaxAddable(CartKey key, RequisitionsEntry entry)
    {
        if (_state == null)
            return 0;

        var byCapacity = GetRemainingCapacity() - GetCartAmount();
        var byBudget = entry.Cost > 0
            ? (_state.Balance - GetCartSupplyTotal()) / entry.Cost
            : byCapacity;
        var byStock = StockRemaining(key, entry);

        return Math.Max(0, Math.Min(Math.Min(byCapacity, byBudget), byStock));
    }

    private int StockRemaining(CartKey key, RequisitionsEntry entry)
    {
        if (entry.Stock < 0)
            return int.MaxValue;

        var bought = _state?.Purchased.GetValueOrDefault(entry.Crate.Id) ?? 0;
        return Math.Max(0, entry.Stock - bought - GetCartAmount(key));
    }

    private string? GetCrateContentsText(RequisitionsEntry entry)
    {
        if (!_prototypes.TryIndex<EntityPrototype>(entry.Crate, out var proto) ||
            !proto.TryGetComponent<StorageFillComponent>("StorageFill", out var fill) ||
            fill.Contents.Count == 0)
        {
            return null;
        }

        var lines = new List<string>();
        foreach (var item in fill.Contents)
        {
            if (item.PrototypeId is not { } pid)
                continue;

            var name = _prototypes.TryIndex<EntityPrototype>(pid, out var itemProto) ? itemProto.Name : pid.Id;
            lines.Add($"{item.Amount}x {name}");
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private void RefreshCart()
    {
        if (_window == null)
            return;

        _window.CartContainer.DisposeAllChildren();

        var search = _window.SearchBar.Text.Trim();
        var scopedItems = 0;
        var visibleItems = 0;
        var items = _cart.Select(pair => (pair.Key, pair.Value)).ToList();

        items.Sort((a, b) =>
        {
            var categoryComparison = a.Key.Category.CompareTo(b.Key.Category);
            return categoryComparison != 0
                ? categoryComparison
                : a.Key.Entry.CompareTo(b.Key.Entry);
        });

        foreach (var (key, amount) in items)
        {
            if (_selectedCategory != null && _selectedCategory != key.Category)
                continue;

            if (!TryGetEntry(key, out var entry))
                continue;

            scopedItems++;
            var display = GetDisplay(entry);
            if (!MatchesSearch(display, search))
                continue;

            visibleItems++;
            var row = new RequisitionsCartRow
            {
                CategoryIndex = key.Category,
                EntryIndex = key.Entry,
                UnitCost = entry.Cost,
                Quantity = { Text = amount.ToString() },
                Cost = { Text = GetLineCostText(entry, amount) },
                Icon =
                {
                    Textures = display.IconTextures,
                    Modulate = display.IconModulate,
                },
            };
            row.ProductName.SetMessage(FormattedMessage.FromUnformatted(display.Name), defaultColor: RequisitionsUiStyles.Green);
            row.Description.SetMessage(FormattedMessage.FromUnformatted(display.Description), defaultColor: RequisitionsUiStyles.TextDim);

            row.AddButton.OnPressed += _ => AddToCart(key);
            row.RemoveButton.OnPressed += _ => RemoveFromCart(key);
            row.AddButton.Disabled = !CanAdd(key, entry);

            _window.CartContainer.AddChild(row);
        }

        var supplyTotal = GetCartSupplyTotal();
        var cartAmount = GetCartAmount();
        var remainingCapacity = GetRemainingCapacity();

        _window.CartTotalLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(
            Loc.GetString("n14-requisitions-cart-total", ("total", supplyTotal))));

        var status = string.Empty;
        if (_cart.Count == 0)
            status = Loc.GetString("n14-requisitions-cart-empty");
        else if (visibleItems == 0 && !string.IsNullOrWhiteSpace(search))
            status = Loc.GetString("n14-requisitions-cart-filter-empty");
        else if (scopedItems == 0)
            status = Loc.GetString("n14-requisitions-cart-category-empty");
        else if (_state != null && supplyTotal > _state.Balance)
            status = Loc.GetString("n14-requisitions-cart-insufficient-funds");
        else if (cartAmount > remainingCapacity)
            status = Loc.GetString("n14-requisitions-cart-insufficient-capacity");

        _window.CartStatusLabel.SetMessage(FormattedMessage.FromUnformatted(status));

        _window.ClearCartButton.Disabled = _cart.Count == 0;
        _window.BuyButton.Disabled = _cart.Count == 0 ||
                                     _state == null ||
                                     supplyTotal > _state.Balance ||
                                     cartAmount > remainingCapacity;
    }

    private void PopulatePendingOrders()
    {
        if (_window == null)
            return;

        _window.PendingContainer.DisposeAllChildren();

        if (_state == null || _state.PendingOrders.Count == 0)
        {
            _window.PendingStatusLabel.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("n14-requisitions-pending-empty")));
            return;
        }

        var search = _window.SearchBar.Text.Trim();
        var visibleOrders = new List<(RequisitionsPendingOrder Order, int? Category, ProductDisplay Display)>();
        foreach (var order in _state.PendingOrders)
        {
            var category = GetPendingOrderCategory(order.Entry);
            if (_selectedCategory != null && category != _selectedCategory)
                continue;

            var display = GetDisplay(order.Entry);
            if (!MatchesSearch(display, search))
                continue;

            visibleOrders.Add((order, category, display));
        }

        visibleOrders.Sort((a, b) =>
        {
            var categoryComparison = (a.Category ?? int.MaxValue).CompareTo(b.Category ?? int.MaxValue);
            if (categoryComparison != 0)
                return categoryComparison;

            return string.Compare(a.Display.Name, b.Display.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        foreach (var (order, _, display) in visibleOrders)
        {
            var card = new RequisitionsPendingOrderCard
            {
                Cost = { Text = GetCostText(order.Entry) },
                Quantity = { Text = Loc.GetString("n14-requisitions-pending-quantity", ("amount", order.Amount)) },
                Icon =
                {
                    Textures = display.IconTextures,
                    Modulate = display.IconModulate,
                },
            };
            card.ProductName.SetMessage(FormattedMessage.FromUnformatted(display.Name), defaultColor: RequisitionsUiStyles.Green);
            card.Description.SetMessage(FormattedMessage.FromUnformatted(display.Description), defaultColor: RequisitionsUiStyles.TextDim);

            _window.PendingContainer.AddChild(card);
        }

        var status = (visibleOrders.Count, string.IsNullOrWhiteSpace(search)) switch
        {
            (0, false) => Loc.GetString("n14-requisitions-pending-filter-empty"),
            (0, true) => Loc.GetString("n14-requisitions-pending-category-empty"),
            _ => string.Empty,
        };
        _window.PendingStatusLabel.SetMessage(FormattedMessage.FromUnformatted(status));
    }

    private void UpdateVisibleProductCards()
    {
        foreach (var (key, card) in _productCards)
        {
            UpdateProductCard(card, key);
        }
    }

    private void UpdateProductCard(RequisitionsProductCard card, CartKey key)
    {
        var amount = GetCartAmount(key);
        card.Quantity.Text = amount.ToString();
        card.RemoveButton.Disabled = amount <= 0;
        var canAdd = TryGetEntry(key, out var entry) && CanAdd(key, entry);
        card.AddButton.Disabled = !canAdd;
        card.Add10Button.Disabled = !canAdd;
        card.MaxButton.Disabled = !canAdd;
    }

    private void BuyCart()
    {
        if (_cart.Count == 0)
            return;

        var items = new List<RequisitionsCartItem>();
        foreach (var (key, amount) in _cart)
        {
            if (amount > 0)
                items.Add(new RequisitionsCartItem(key.Category, key.Entry, amount));
        }

        if (items.Count == 0)
            return;

        SendMessage(new RequisitionsBuyCartMsg(items));

        _cart.Clear();
        RefreshCart();
        UpdateVisibleProductCards();
    }

    private int? GetPendingOrderCategory(RequisitionsEntry entry)
    {
        if (_state is not { } computer)
            return null;

        for (var categoryIndex = 0; categoryIndex < computer.Categories.Count; categoryIndex++)
        {
            var category = computer.Categories[categoryIndex];
            foreach (var categoryEntry in category.Entries)
            {
                if (SamePendingEntry(categoryEntry, entry))
                    return categoryIndex;
            }
        }

        return null;
    }

    private static bool SamePendingEntry(RequisitionsEntry a, RequisitionsEntry b)
    {
        if (a.Crate != b.Crate ||
            a.Cost != b.Cost ||
            a.Name != b.Name ||
            a.Description != b.Description ||
            !Equals(a.Icon, b.Icon) ||
            a.Entities.Count != b.Entities.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Entities.Count; i++)
        {
            if (a.Entities[i] != b.Entities[i])
                return false;
        }

        return true;
    }

    private bool TryGetEntry(CartKey key, out RequisitionsEntry entry)
    {
        entry = default!;

        if (_state is not { } computer || key.Category < 0 || key.Category >= computer.Categories.Count)
            return false;

        var category = computer.Categories[key.Category];
        if (key.Entry < 0 || key.Entry >= category.Entries.Count)
            return false;

        entry = category.Entries[key.Entry];
        return true;
    }

    private ProductDisplay GetDisplay(RequisitionsEntry entry)
    {
        _prototypes.TryIndex<EntityPrototype>(entry.Crate, out var prototype);

        var name = prototype?.Name ?? entry.Crate.ToString();
        if (entry.Name is { } nameOverride && Loc.TryGetString(nameOverride, out var localizedName))
            name = localizedName;

        var description = prototype?.Description ?? string.Empty;
        if (entry.Description is { } descriptionOverride && Loc.TryGetString(descriptionOverride, out var localizedDescription))
            description = localizedDescription;
        if (string.IsNullOrWhiteSpace(description))
            description = Loc.GetString("n14-requisitions-card-no-description");

        var icon = GetDisplayIcon(entry.Icon, prototype);

        return new ProductDisplay(name, description, icon.Textures, icon.Modulate);
    }

    private DisplayIcon GetDisplayIcon(SpriteSpecifier? icon, EntityPrototype? fallbackPrototype)
    {
        if (icon is SpriteSpecifier.EntityPrototype entityIcon &&
            _prototypes.TryIndex<EntityPrototype>(entityIcon.EntityPrototypeId, out var iconPrototype))
        {
            return GetPrototypeDisplayIcon(iconPrototype);
        }

        if (icon != null)
            return new DisplayIcon(new List<Texture> { _sprite.Frame0(icon) }, Color.White);

        if (fallbackPrototype != null)
            return GetPrototypeDisplayIcon(fallbackPrototype);

        return new DisplayIcon(new List<Texture>(), Color.White);
    }

    private DisplayIcon GetPrototypeDisplayIcon(EntityPrototype prototype)
    {
        var textures = SpriteComponent.GetPrototypeTextures(prototype, _resources)
            .Select(texture => texture.Default)
            .ToList();

        var modulate = Color.White;
        if (prototype.TryGetComponent<SpriteComponent>("Sprite", out var sprite) &&
            sprite.AllLayers.Any())
        {
            modulate = sprite.AllLayers.First().Color;
        }

        return new DisplayIcon(textures, modulate);
    }

    private static bool MatchesSearch(ProductDisplay display, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        return display.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string GetCostText(RequisitionsEntry entry)
    {
        return Loc.GetString("n14-requisitions-card-cost", ("cost", entry.Cost));
    }

    private static string GetLineCostText(RequisitionsEntry entry, int amount)
    {
        return Loc.GetString("n14-requisitions-cart-row-cost", ("cost", entry.Cost * amount));
    }

    private int GetCartAmount(CartKey key)
    {
        return _cart.GetValueOrDefault(key);
    }

    private int GetCartAmount()
    {
        var amount = 0;
        foreach (var count in _cart.Values)
        {
            amount += count;
        }

        return amount;
    }

    private int GetCartSupplyTotal()
    {
        var total = 0;
        foreach (var (key, amount) in _cart)
        {
            if (TryGetEntry(key, out var entry))
                total += entry.Cost * amount;
        }

        return total;
    }

    private int GetRemainingCapacity()
    {
        if (_state == null)
            return 0;

        return Math.Max(0, _state.Capacity - _state.OrderCount);
    }

    private readonly record struct CartKey(int Category, int Entry);

    private readonly record struct DisplayIcon(List<Texture> Textures, Color Modulate);

    private readonly record struct ProductDisplay(string Name, string Description, List<Texture> IconTextures, Color IconModulate);
}
