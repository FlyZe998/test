using System.Collections.Generic;

namespace KeySpammer;

/// <summary>Display name -> Windows virtual-key code, ordered for a sane dropdown.</summary>
internal static class KeyMap
{
    public static readonly List<KeyValuePair<string, ushort>> All = Build();

    private static List<KeyValuePair<string, ushort>> Build()
    {
        var list = new List<KeyValuePair<string, ushort>>();

        void Add(string name, ushort vk) => list.Add(new(name, vk));

        // Letters A-Z
        for (char c = 'A'; c <= 'Z'; c++)
            Add(c.ToString(), (ushort)c);

        // Digits 0-9 (top row)
        for (char c = '0'; c <= '9'; c++)
            Add(c.ToString(), (ushort)c);

        // Function keys F1-F24
        for (int i = 1; i <= 24; i++)
            Add($"F{i}", (ushort)(0x70 + (i - 1)));

        // Whitespace / editing
        Add("Space", 0x20);
        Add("Enter", 0x0D);
        Add("Tab", 0x09);
        Add("Backspace", 0x08);
        Add("Escape", 0x1B);
        Add("Delete", 0x2E);
        Add("Insert", 0x2D);

        // Navigation
        Add("Home", 0x24);
        Add("End", 0x23);
        Add("Page Up", 0x21);
        Add("Page Down", 0x22);
        Add("Arrow Up", 0x26);
        Add("Arrow Down", 0x28);
        Add("Arrow Left", 0x25);
        Add("Arrow Right", 0x27);

        // Modifiers
        Add("Shift (Left)", 0xA0);
        Add("Shift (Right)", 0xA1);
        Add("Ctrl (Left)", 0xA2);
        Add("Ctrl (Right)", 0xA3);
        Add("Alt (Left)", 0xA4);
        Add("Alt (Right)", 0xA5);
        Add("Caps Lock", 0x14);

        // Numpad
        for (int i = 0; i <= 9; i++)
            Add($"Numpad {i}", (ushort)(0x60 + i));
        Add("Numpad *", 0x6A);
        Add("Numpad +", 0x6B);
        Add("Numpad -", 0x6D);
        Add("Numpad .", 0x6E);
        Add("Numpad /", 0x6F);
        Add("Num Lock", 0x90);

        // Punctuation (US layout OEM codes)
        Add(";", 0xBA);
        Add("=", 0xBB);
        Add(",", 0xBC);
        Add("-", 0xBD);
        Add(".", 0xBE);
        Add("/", 0xBF);
        Add("`", 0xC0);
        Add("[", 0xDB);
        Add("\\", 0xDC);
        Add("]", 0xDD);
        Add("'", 0xDE);

        Add("Print Screen", 0x2C);
        Add("Scroll Lock", 0x91);
        Add("Pause", 0x13);

        return list;
    }
}
