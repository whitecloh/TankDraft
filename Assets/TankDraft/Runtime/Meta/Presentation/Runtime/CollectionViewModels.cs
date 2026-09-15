using UnityEngine;

namespace TankDraft.UI
{
    public sealed class CardViewModel
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string Subtitle;
        public readonly Sprite Icon;
        public readonly bool Interactable;
        public readonly bool Selected;

        public CardViewModel(string id, string title, string subtitle, Sprite icon, bool interactable, bool selected)
        {
            Id = id;
            Title = title;
            Subtitle = subtitle;
            Icon = icon;
            Interactable = interactable;
            Selected = selected;
        }
    }

    public sealed class CollectionViewModel
    {
        public readonly CardViewModel[] Loadout;
        public readonly CardViewModel[] Catalog;
        public readonly string UnitsLabel;
        public readonly string OrdersLabel;
        public readonly string Heading;
        public readonly string Summary;
        public readonly bool Orders;

        public CollectionViewModel(CardViewModel[] loadout, CardViewModel[] catalog, string unitsLabel, string ordersLabel, string heading, string summary, bool orders)
        {
            Loadout = loadout;
            Catalog = catalog;
            UnitsLabel = unitsLabel;
            OrdersLabel = ordersLabel;
            Heading = heading;
            Summary = summary;
            Orders = orders;
        }
    }

    public sealed class CardDetailsViewModel
    {
        public readonly string Title;
        public readonly string Description;
        public readonly string ActionLabel;
        public readonly string CloseLabel;
        public readonly string SlotHeading;
        public readonly Sprite Icon;
        public readonly bool CanEquip;
        public readonly bool ChoosingSlot;
        public readonly CardViewModel[] Slots;
        public readonly bool CanProgression;

        public CardDetailsViewModel(string title, string description, string actionLabel, string closeLabel, string slotHeading, Sprite icon, bool canEquip, bool choosingSlot, CardViewModel[] slots, bool canProgression = false)
        {
            Title = title;
            Description = description;
            ActionLabel = actionLabel;
            CloseLabel = closeLabel;
            SlotHeading = slotHeading;
            Icon = icon;
            CanEquip = canEquip;
            ChoosingSlot = choosingSlot;
            Slots = slots;
            CanProgression = canProgression;
        }
    }
}
