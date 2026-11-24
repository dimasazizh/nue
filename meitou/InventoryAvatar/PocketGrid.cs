using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Data.Ui;
using Meitou.Simulation.Items;

namespace Nue.InventoryAvatar;

public sealed partial class AvatarMod
{
    private static int pocketColumns, pocketRows;
    [ThreadStatic] private static int loadingColumns;

    private static void ConfigurePockets(MyGuiWidget root)
    {
        pocketColumns = Dimension("PocketColumns");
        pocketRows = Dimension("PocketRows");
        int Dimension(string key) => int.TryParse(root.UserStrings.GetValueOrDefault(key), out int n) && n > 0
            ? n : throw new InvalidDataException($"InventoryInspection.layout needs a positive {key}.");
    }

    private static void PocketSize(GameRecord? record, ref (int Width, int Height) __result)
    {
        if (record?.Type != FcsRecordType.AnimalCharacter)
            __result = (Math.Max(loadingColumns, pocketColumns), pocketRows);
    }

    // Let the native loader validate everything at its saved position first. It already
    // grows the height, but would reject items outside a newly narrowed width.
    private static void BeforeLoad(IReadOnlyList<SaveItem> items, ItemFactory factory, out int __state)
    {
        __state = loadingColumns;
        loadingColumns = 0;
        foreach (var item in items)
            if (item.Section == "main" && factory.Info(item.BaseId) is {} info)
                loadingColumns = Math.Max(loadingColumns, item.X + (item.Rotated ? info.Height : info.Width));
    }

    private static void EndLoad(int __state) => loadingColumns = __state;

    private static void FitSavedPockets(GameRecord? owner, ref Inventory __result)
    {
        if (owner?.Type == FcsRecordType.AnimalCharacter ||
            (__result.Width == pocketColumns && __result.Height == pocketRows)) return;
        var staged = new Inventory(pocketColumns, pocketRows);
        var pockets = __result.Items.Where(i => i.Section == "main").ToList();
        var moves = new List<(ItemInstance Item, int X, int Y, bool Turn)>();
        bool Inside(ItemInstance i) => i.X+i.Width <= pocketColumns && i.Y+i.Height <= pocketRows;
        staged.Items.AddRange(pockets.Where(Inside));
        foreach (var item in pockets.Where(i => !Inside(i)).OrderByDescending(i => i.Width*i.Height))
        {
            bool turn = false;
            if (!staged.FindCell("main", item.Width, item.Height, out int x, out int y))
            {
                if (!staged.FindCell("main", item.Height, item.Width, out x, out y))
                {
                    // A full legacy 8x10 still fits by transposing every rectangle.
                    // For oversized modded inventories keep the loaded grid intact.
                    if (!pockets.All(i => i.Y+i.Height <= pocketColumns && i.X+i.Width <= pocketRows)) return;
                    moves = pockets.Select(i => (i, i.Y, i.X, i.Width != i.Height)).ToList();
                    break;
                }
                turn = true;
            }
            var placeholder = new ItemInstance { Record = item.Record, Section = "main", X = x, Y = y,
                Width = turn ? item.Height : item.Width, Height = turn ? item.Width : item.Height };
            staged.Items.Add(placeholder);
            moves.Add((item,x,y,turn));
        }
        foreach (var (item,x,y,turn) in moves)
        {
            item.X = x;
            item.Y = y;
            if (turn) item.Rotated = !item.Rotated;
        }
        staged.Items.Clear();
        staged.Items.AddRange(__result.Items);
        __result = staged;
    }
}
