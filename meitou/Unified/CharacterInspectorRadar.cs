using System.Collections;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Nue.Unified;

internal static partial class CharacterInspector
{
    private static ServiceRecordRadar? serviceRecord;
    internal sealed record RadarData(string Texture, string Caption, (string Label, string Value)[] Axes);

    private static void DrawRadar(List<UiControl> controls, CharacterSnapshot character, UiRect area, View view)
    {
        if (area.Width <= 0 || area.Height <= 0) return;
        long now = Environment.TickCount64;
        if (now >= view.RefreshRadarAt)
        {
            view.RefreshRadarAt = now + 1000;
            view.Radar = serviceRecord?.Read(character.Id);
        }
        string font = Math.Max(1, (int)(area.Height * .067f)).ToString(CultureInfo.InvariantCulture);
        var radar = view.Radar;
        if (radar is null || radar.Texture.Length == 0)
        {
            string message = radar?.Caption ?? (serviceRecord is null
                ? "Requires PZA’s Service Record."
                : "No service record for this character.");
            AddText(controls, "radar-empty", area, message, font, "Center VCenter");
            return;
        }

        float row = area.Height * .105f;
        float side = Math.Min(area.Width * .61f, area.Height - row * 2);
        var centre = new System.Numerics.Vector2(area.X + area.Width / 2, area.Y + (area.Height - row) / 2);
        var image = new UiRect(centre.X - side / 2, centre.Y - side / 2, side, side);
        var properties = UiTooltips.RowProperties(radar.Axes);
        properties["ImageTexture"] = radar.Texture;
        controls.Add(new(ActionPrefix + "radar", "ImageBox", image, "", "", "", true,
            properties, default, Pickable: true, Clip: area));

        for (int axis = 0; axis < radar.Axes.Length; axis++)
        {
            double angle = axis * Math.Tau / radar.Axes.Length;
            float x = centre.X + (float)Math.Sin(angle) * side * .56f;
            float y = centre.Y - (float)Math.Cos(angle) * side * .48f;
            float labelWidth = area.Width * .31f;
            float labelX = Math.Clamp(x - labelWidth / 2, area.X, area.X + area.Width - labelWidth);
            var label = new UiRect(labelX, y - row / 2, labelWidth, row);
            AddText(controls, "radar-axis-" + axis, label, radar.Axes[axis].Label, font, "Center VCenter", Gold);
        }
        AddText(controls, "radar-mode", area with { Y = area.Y + area.Height - row, Height = row },
            radar.Caption, font, "Center VCenter");
    }

    // Bridge to PZA’s Service Record for Meitou. Its recorder, squad snapshot, scoring, and image cache stay in that mod.
    private sealed class ServiceRecordRadar
    {
        private const string Namespace = "PZA.ServiceRecord.Meitou.";
        private readonly object core, sync;
        private readonly FieldInfo squad, memberKey, memberStats, values, known, perFight, fights;
        private readonly MethodInfo key, find, raw, score, render, label, format;
        private readonly Type rawType;

        private ServiceRecordRadar(Assembly assembly)
        {
            Type TypeOf(string name) => assembly.GetType(Namespace + name, throwOnError: true)!;
            var system = TypeOf("SrSystem");
            var member = TypeOf("SrWindow+Person");
            var radar = TypeOf("SrRadar");
            rawType = TypeOf("SrRadar+Raw");
            core = system.GetField("Core")!.GetValue(null)!;
            sync = system.GetField("Lock")!.GetValue(null)!;
            squad = TypeOf("SrWindow").GetField("s_squad", BindingFlags.Static | BindingFlags.NonPublic)!;
            memberKey = member.GetField("Key")!;
            memberStats = member.GetField("S")!;
            values = rawType.GetField("V")!;
            known = rawType.GetField("Known")!;
            perFight = rawType.GetField("PerFight")!;
            fights = rawType.GetField("Fights")!;
            key = TypeOf("SrBridge").GetMethod("Key", [typeof(CharacterId)])!;
            find = core.GetType().GetMethod("Find", [typeof(ulong)])!;
            raw = radar.GetMethod("Of")!;
            score = radar.GetMethod("Score")!;
            label = radar.GetMethod("AxisLabel")!;
            format = radar.GetMethod("WithUnit")!;
            render = TypeOf("SrRadarImage").GetMethod("For", [typeof(double[]), typeof(bool[])])!;
            if (new MemberInfo?[] { squad, memberKey, memberStats, values, known, perFight, fights,
                    key, find, raw, score, label, format, render }.Any(memberInfo => memberInfo is null))
                throw new MissingMemberException("The Service Record chart interface has changed.");
        }

        internal static ServiceRecordRadar? Connect()
        {
            if (AccessTools.TypeByName(Namespace + "SrRadar") is not {} radar) return null;
            try { return new ServiceRecordRadar(radar.Assembly); }
            catch (Exception error) when (error is TypeLoadException or MissingMemberException or
                NullReferenceException or TypeInitializationException) { return null; }
        }

        internal RadarData? Read(CharacterId character)
        {
            try
            {
                ulong id = (ulong)key.Invoke(null, [character])!;
                object me;
                var others = new List<object>();
                lock (sync)
                {
                    if (find.Invoke(core, [id]) is not {} stats) return null;
                    me = raw.Invoke(null, [stats])!;
                    if (squad.GetValue(null) is IEnumerable members)
                        foreach (object member in members)
                            if ((ulong)memberKey.GetValue(member)! != id)
                                others.Add(raw.Invoke(null, [memberStats.GetValue(member)])!);
                }
                var pool = Array.CreateInstance(rawType, others.Count);
                for (int i = 0; i < others.Count; i++) pool.SetValue(others[i], i);
                var numbers = (double[])values.GetValue(me)!;
                var available = (bool[])known.GetValue(me)!;
                bool fightRates = (bool)perFight.GetValue(me)!;
                var scaled = new double[numbers.Length];
                score.Invoke(null, [pool, me, scaled, .04d]);
                var texture = (string)render.Invoke(null, [scaled, available])!;
                if (texture.Length == 0) return new("", "Service Record chart unavailable.", []);
                var axes = numbers.Select((value, i) => ((string)label.Invoke(null, [i])!,
                    available[i] ? (string)format.Invoke(null, [i, fightRates, value])! : "—")).ToArray();
                string caption = fightRates ? $"Per fight · {fights.GetValue(me)} recorded" : "Career totals";
                return new(texture, caption, axes);
            }
            catch (Exception error) when (error is TargetInvocationException or ArgumentException or InvalidCastException)
            {
                return new("", "Service Record chart unavailable.", []);
            }
        }
    }
}
