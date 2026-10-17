using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BulkIdentificationRequests;
using HarmonyLib;
using SeaPower;
using SeapowerUI;

// Execute the game's actual request and communication IL against managed engine
// boundaries. Its radio, compliance and disclosure algorithms are not copied here.
internal static class GameCallbackFixture
{
    public class UnitObject
    {
        public object Force { get; set; } = new object();
        public bool UnitKind = true;
        public bool Submerged;
        public Identification Identified { get; } = new Identification();
        public Brain? Ai = new Brain();
        public float VisibleAltitude => 0;
        public Transform Transform { get; } = new Transform();
        public UnitObject GetBaseObject() => this;
        public bool IsUnit() => UnitKind;
        public bool IsSubmerged() => Submerged;
    }
    public sealed class Identification { public bool Value { get; set; } }
    public sealed class Bio : UnitObject { }
    public sealed class Brain { public int Compliance { get; set; } = 3; }
    public sealed class Transform { public Vector Position { get; set; } }
    public struct Vector
    {
        public float Value;
        public float Magnitude => Math.Abs(Value);
    }
    public struct Sight { public bool Visible; }
    public sealed class Renderer { public UnitObject? Selected; public UnitObject? GetSelectedObject() => Selected; }
    public sealed class Selection { public object? Value { get; set; } }
    public sealed class Scene { public Selection SelectedObject = new Selection(); }
    public sealed class Game
    {
        public Scene Map { get; } = new Scene();
        public readonly List<(UnitObject Sender, string Message)> Alerts = new List<(UnitObject, string)>();
        public void RaiseAlert(UnitObject sender, string text) => Alerts.Add((sender, text));
    }
    public sealed class Language { public string GetText(string section, string key, bool first, bool second) => key; }
    public sealed class Contact
    {
        public UnitObject? Unit { get; set; }
        public object OurTaskforce { get; set; } = new object();
        public MapVisualType VisualType { get; set; } = MapVisualType.Surface;
        public MapVisualPosture VisualPosture { get; set; } = MapVisualPosture.Unknown;
        public UnitObject? CommunicationResult(UnitObject target, string key, out UnitObject? sender) => communicate(this, target, key, out sender);
    }
    private delegate UnitObject? Communicate(Contact contact, UnitObject target, string key, out UnitObject? sender);
    private static Communicate communicate = null!;
    private static Action<Contact, object> identify = null!;
    private static Func<Contact, bool> hasRequestOption = null!;
    public static Game CurrentGame = new Game();
    public static object PlayerForce = new object();
    private static Renderer renderer = new Renderer();
    private static readonly Language language = new Language();
    private static bool sight;
    private static float horizon;
    private static readonly List<(UnitObject Observer, UnitObject Subject)> disclosures = new List<(UnitObject, UnitObject)>();

    public static Renderer GetRenderer() => renderer;
    public static Language GetLanguage() => language;
    public static UnitObject? ResolveSender(Renderer view, Contact contact) => RequestSource<UnitObject, Contact>.For(contact) ?? view.GetSelectedObject();
    public static bool Equal(UnitObject? first, object? second) => ReferenceEquals(first, second);
    public static bool HasRequestOption(Contact target) => hasRequestOption(target);
    public static Sight LineOfSight(UnitObject first, UnitObject second, float altitude) => new Sight { Visible = sight };
    public static float CommPosition(UnitObject unit) => 1;
    public static float TerrainAltitude(UnitObject first, UnitObject second) => 0;
    public static float Horizon(float first, float second, bool surface, float terrain) => horizon;
    public static Vector Subtract(Vector first, Vector second) => new Vector { Value = first.Value - second.Value };
    public static string Format(UnitObject target, string text) => text;
    public static void Reveal(UnitObject observer, UnitObject subject, string sensor, int level) => disclosures.Add((observer, subject));

    internal static void Run()
    {
        var menu = InstructionReader.Read(typeof(MapUnitViewModel).GetMethod(nameof(MapUnitViewModel.GetContextMenuItems))!);
        var callback = VanillaRequests.FindCallback(menu);
        var originalCommunication = InstructionReader.Read(VanillaRequests.Communication);
        var rewritten = VanillaRequests.Rewrite(originalCommunication, VanillaRequests.SelectedObject, VanillaRequests.Resolver);
        communicate = (Communicate)Compile(VanillaRequests.Communication, rewritten, typeof(Communicate),
            typeof(UnitObject), new[] { typeof(Contact), typeof(UnitObject), typeof(string), typeof(UnitObject).MakeByRefType() });
        identify = (Action<Contact, object>)Compile(callback, InstructionReader.Read(callback), typeof(Action<Contact, object>),
            typeof(void), new[] { typeof(Contact), typeof(object) });

        TargetEligibility();

        var (sender, contact) = Reset();
        UnitObject selected = renderer.Selected!;
        RequestSource<UnitObject, Contact>.Run(sender, contact, () => identify(contact, null!));
        Program.Require(ReferenceEquals(renderer.Selected, selected), "Bulk requests must preserve the player's selection.");
        Program.Require(disclosures.Count == 2 && ReferenceEquals(disclosures[0].Observer, contact.Unit) &&
            ReferenceEquals(disclosures[0].Subject, sender) && ReferenceEquals(disclosures[1].Observer, sender) &&
            ReferenceEquals(disclosures[1].Subject, contact.Unit), "A successful request must expose the clicked sender and reveal the complying contact in the standard directions.");
        Program.Require(CurrentGame.Alerts.Select(alert => alert.Message).SequenceEqual(new[] { "IdentificationRequest", "FollowRequest" }), "The standard request/response messages must be preserved.");

        (sender, contact) = Reset();
        selected = renderer.Selected!;
        identify(contact, null!);
        Program.Require(disclosures.Count == 2 && ReferenceEquals(disclosures[0].Subject, selected), "Ordinary manual requests must keep using the selected unit.");

        foreach (int compliance in new[] { 1, 2 })
        {
            (sender, contact) = Reset();
            contact.Unit!.Ai!.Compliance = compliance;
            RequestSource<UnitObject, Contact>.Run(sender, contact, () => identify(contact, null!));
            Program.Require(disclosures.Count == 1 && ReferenceEquals(disclosures[0].Subject, sender), "Noncompliance must expose the sender but must not reveal the contact.");
            Program.Require(CurrentGame.Alerts.Last().Message == (compliance == 1 ? "IgnoreComm" : "RefuseRequest"), "The original noncompliance response must be preserved.");
        }
        (sender, contact) = Reset();
        sight = false;
        RequestSource<UnitObject, Contact>.Run(sender, contact, () => identify(contact, null!));
        Program.Require(disclosures.Count == 0 && CurrentGame.Alerts.Single().Message == "NoLOS", "Blocked line of sight must prevent transmission and identification.");

        (sender, contact) = Reset();
        contact.Unit!.Transform.Position = new Vector { Value = 13 };
        horizon = 10;
        RequestSource<UnitObject, Contact>.Run(sender, contact, () => identify(contact, null!));
        Program.Require(disclosures.Count == 0 && CurrentGame.Alerts.Single().Message == "NoLOS", "A contact beyond the game's radio horizon must not receive a request.");

        (sender, contact) = Reset(); sender.Submerged = true;
        RequestSource<UnitObject, Contact>.Run(sender, contact, () => identify(contact, null!));
        Program.Require(disclosures.Count == 0 && CurrentGame.Alerts.Count == 0, "A submerged sender must not transmit.");
        (sender, contact) = Reset(); contact.Unit!.Submerged = true;
        RequestSource<UnitObject, Contact>.Run(sender, contact, () => identify(contact, null!));
        Program.Require(disclosures.Count == 0 && CurrentGame.Alerts.Count == 0, "A submerged target must not be interrogated.");
        (sender, contact) = Reset(); sender.Force = new object();
        RequestSource<UnitObject, Contact>.Run(sender, contact, () => identify(contact, null!));
        Program.Require(disclosures.Count == 0 && CurrentGame.Alerts.Count == 0, "A foreign sender must fail the game's ownership check.");
        (sender, contact) = Reset(); sender.UnitKind = false;
        RequestSource<UnitObject, Contact>.Run(sender, contact, () => identify(contact, null!));
        Program.Require(disclosures.Count == 0 && CurrentGame.Alerts.Count == 0, "A non-unit sender must fail the game's unit check.");
        (sender, contact) = Reset(); contact.Unit!.Ai = null;
        RequestSource<UnitObject, Contact>.Run(sender, contact, () => identify(contact, null!));
        Program.Require(disclosures.Count == 1 && CurrentGame.Alerts.Last().Message == "IgnoreComm", "A target without an AI response must not be forced to identify.");
        (sender, contact) = Reset(); contact.Unit = null;
        RequestSource<UnitObject, Contact>.Run(sender, contact, () => identify(contact, null!));
        Program.Require(disclosures.Count == 0, "A removed contact must not cause a disclosure.");
        (sender, contact) = Reset(); renderer.Selected = null;
        CurrentGame.Map.SelectedObject.Value = sender;
        identify(contact, null!);
        Program.Require(disclosures.Count == 2 && ReferenceEquals(disclosures[0].Subject, sender), "The game's map-selection fallback must remain intact for manual requests.");
        Program.Throws<InvalidOperationException>(() => VanillaRequests.Rewrite(Array.Empty<CodeInstruction>(), VanillaRequests.SelectedObject, VanillaRequests.Resolver));
        Console.WriteLine("PASS actual game IL: clicked/manual sender, selection preservation, acceptance/refusal/nonresponse, LOS/radio horizon, submerged/foreign/non-unit senders and absent contacts.");
    }

    private static void TargetEligibility()
    {
        var option = typeof(VanillaRequests).GetMethod(nameof(VanillaRequests.HasRequestOption), BindingFlags.NonPublic | BindingFlags.Static)!;
        hasRequestOption = (Func<Contact, bool>)Compile(option, InstructionReader.Read(option), typeof(Func<Contact, bool>),
            typeof(bool), new[] { typeof(Contact) });
        var filter = typeof(BulkRequests).GetMethod(nameof(BulkRequests.Eligible), BindingFlags.NonPublic | BindingFlags.Static)!;
        var eligible = (Func<Contact, object, bool>)Compile(filter, InstructionReader.Read(filter), typeof(Func<Contact, object, bool>),
            typeof(bool), new[] { typeof(Contact), typeof(object) });

        var player = new object();
        var target = new Contact { Unit = new UnitObject(), OurTaskforce = player };
        foreach (MapVisualType type in Enum.GetValues(typeof(MapVisualType)))
        {
            target.VisualType = type;
            Program.Require(eligible(target, player), "A contact with the standard request option must remain eligible regardless of its displayed type: " + type);
        }
        target.VisualType = MapVisualType.Air;
        foreach (var posture in new[] { MapVisualPosture.Allied, MapVisualPosture.Enemy, MapVisualPosture.Neutral })
        {
            target.VisualPosture = posture;
            Program.Require(!eligible(target, player), "An aircraft with known allegiance must not receive a bulk request.");
        }
        target.VisualPosture = MapVisualPosture.Unknown;
        target.Unit.Identified.Value = true;
        Program.Require(!eligible(target, player), "An already identified aircraft must not receive a bulk request.");
        target.Unit.Identified.Value = false;
        target.OurTaskforce = new object();
        Program.Require(!eligible(target, player), "Another task force's contact must not enter the player's batch.");
        target.OurTaskforce = player;
        target.VisualType = MapVisualType.Submarine;
        target.Unit.Submerged = true;
        Program.Require(!eligible(target, player), "A submerged target without the standard menu option must be excluded.");
        target.Unit.Submerged = false;
        Program.Require(eligible(target, player), "A surfaced target must not be excluded by its submarine display type.");
        target.Unit = null;
        Program.Require(!eligible(target, player), "A removed target must not enter the batch.");
        Console.WriteLine("PASS production target eligibility: all contact types, unknown allegiance, identification, task force, submerged and absent targets.");
    }

    private static (UnitObject Sender, Contact Contact) Reset()
    {
        CurrentGame = new Game(); PlayerForce = new object(); sight = true; horizon = 100;
        renderer = new Renderer { Selected = new UnitObject { Force = PlayerForce } };
        disclosures.Clear();
        return (new UnitObject { Force = PlayerForce }, new Contact { Unit = new UnitObject() });
    }

    private static Delegate Compile(MethodInfo original, List<CodeInstruction> code, Type delegateType, Type result, Type[] arguments)
    {
        var method = new DynamicMethod("Managed_" + original.Name, result, arguments, typeof(GameCallbackFixture).Module, true);
        var generator = method.GetILGenerator();
        var locals = original.GetMethodBody()!.LocalVariables.Select(local => generator.DeclareLocal(MapType(local.LocalType), local.IsPinned)).ToArray();
        var labels = new Dictionary<Label, Label>();
        Label Translate(Label label)
        {
            if (!labels.TryGetValue(label, out Label mapped)) { mapped = generator.DefineLabel(); labels.Add(label, mapped); }
            return mapped;
        }
        foreach (var instruction in code)
        {
            if (instruction.blocks.Count != 0) throw new NotSupportedException("Unexpected exception handler in the game's request path.");
            foreach (Label label in (IList)typeof(CodeInstruction).GetField("labels")!.GetValue(instruction)!) generator.MarkLabel(Translate(label));
            object? operand = instruction.operand;
            OpCode opcode = instruction.opcode;
            if (operand == null) generator.Emit(opcode);
            else if (operand is Label target) generator.Emit(opcode, Translate(target));
            else if (operand is Label[] targets) generator.Emit(opcode, targets.Select(Translate).ToArray());
            else if (operand is MethodInfo called) generator.Emit(opcode, MapMethod(called));
            else if (operand is FieldInfo field) generator.Emit(opcode, MapField(field));
            else if (operand is Type type) generator.Emit(opcode, MapType(type));
            else if (operand is int index && (opcode.OperandType == OperandType.ShortInlineVar || opcode.OperandType == OperandType.InlineVar))
            {
                if (opcode.Name!.Contains("loc")) generator.Emit(opcode, locals[index]);
                else generator.Emit(opcode, (short)index);
            }
            else if (operand is int integer) generator.Emit(opcode, integer);
            else if (operand is sbyte small) generator.Emit(opcode, small);
            else if (operand is byte unsigned) generator.Emit(opcode, unsigned);
            else if (operand is float number) generator.Emit(opcode, number);
            else if (operand is string text) generator.Emit(opcode, text);
            else throw new NotSupportedException("Unmapped request operand: " + operand);
        }
        return method.CreateDelegate(delegateType);
    }

    private static Type MapType(Type type)
    {
        if (type == typeof(ObjectBase) || type == typeof(Vehicle) || type.FullName == "UnityEngine.Component") return typeof(UnitObject);
        if (type.FullName == "SeaPower.Biologic") return typeof(Bio);
        if (type == typeof(MapUnitViewModel)) return typeof(Contact);
        if (type == typeof(MapVisualType) || type == typeof(MapVisualPosture)) return type;
        if (type == typeof(RenderPosition)) return typeof(Renderer);
        if (type.FullName == "SeaPower.AI") return typeof(Brain);
        if (type.FullName == "UnityEngine.Vector3") return typeof(Vector);
        if (type.FullName == "UnityEngine.Transform") return typeof(Transform);
        if (type.FullName == "SeaPower.LineOfSightResult") return typeof(Sight);
        if (type.FullName == "SeaPower.Taskforce") return typeof(object);
        if (type.Namespace != null && (type.Namespace.StartsWith("SeaPower", StringComparison.Ordinal) || type.Namespace.StartsWith("SeapowerUI", StringComparison.Ordinal) || type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal)))
            throw new NotSupportedException("Unmapped game type: " + type);
        return type;
    }
    private static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)!;
    private static MethodInfo Getter(Type type, string name) => type.GetProperty(name)!.GetGetMethod()!;
    private static MethodInfo Own(string name) => Method(typeof(GameCallbackFixture), name);
    private static FieldInfo Field(Type type, string name) => type.GetField(name)!;
    private static FieldInfo MapField(FieldInfo field)
    {
        if (field.DeclaringType == typeof(Globals) && field.Name == "_mainGameViewModel") return Field(typeof(GameCallbackFixture), nameof(CurrentGame));
        if (field.DeclaringType == typeof(Globals) && field.Name == "_playerTaskforce") return Field(typeof(GameCallbackFixture), nameof(PlayerForce));
        if (field.DeclaringType == typeof(ObjectBase) && field.Name == "_ai") return Field(typeof(UnitObject), nameof(UnitObject.Ai));
        if (field.DeclaringType!.FullName == "SeapowerUI.ViewModels.SceneMapViewModel" && field.Name == "SelectedObject") return Field(typeof(Scene), nameof(Scene.SelectedObject));
        if (field.DeclaringType.FullName == "SeaPower.LineOfSightResult" && field.Name == "_los") return Field(typeof(Sight), nameof(Sight.Visible));
        throw new NotSupportedException("Unmapped game field: " + field);
    }
    private static MethodInfo MapMethod(MethodInfo method)
    {
        string type = method.DeclaringType!.FullName!;
        string name = method.Name;
        if (method == VanillaRequests.Resolver) return Own(nameof(ResolveSender));
        if (method.DeclaringType == typeof(VanillaRequests) && name == nameof(VanillaRequests.HasRequestOption)) return Own(nameof(HasRequestOption));
        if (method.DeclaringType == typeof(ContactSelection) || method.DeclaringType == typeof(object)) return method;
        if (method == VanillaRequests.SelectedObject) return Method(typeof(Renderer), nameof(Renderer.GetSelectedObject));
        if (method == VanillaRequests.Communication) return Method(typeof(Contact), nameof(Contact.CommunicationResult));
        if (type.StartsWith("SeaPower.Singleton`1", StringComparison.Ordinal) && name == "get_Instance")
        {
            if (method.ReturnType == typeof(RenderPosition)) return Own(nameof(GetRenderer));
            if (method.ReturnType == typeof(LanguageResourceHandler)) return Own(nameof(GetLanguage));
        }
        if (type == "SeapowerUI.MapUnitViewModel" && name == "get_Unit") return Getter(typeof(Contact), nameof(Contact.Unit));
        if (type == "SeapowerUI.MapUnitViewModel" && name == "get_OurTaskforce") return Getter(typeof(Contact), nameof(Contact.OurTaskforce));
        if (type == "SeapowerUI.MapUnitViewModel" && name == "get_VisualType") return Getter(typeof(Contact), nameof(Contact.VisualType));
        if (type == "SeapowerUI.MapUnitViewModel" && name == "get_VisualPosture") return Getter(typeof(Contact), nameof(Contact.VisualPosture));
        if (type == "SeaPower.Vehicle" && name == "get_Identified") return Getter(typeof(UnitObject), nameof(UnitObject.Identified));
        if (type == "SeapowerUI.MapUnitViewModel" && name == "FormatCommText") return Own(nameof(Format));
        if (type == "SeaPower.Vehicle" && name == "get_BaseObject") return Method(typeof(UnitObject), nameof(UnitObject.GetBaseObject));
        if (type == "UnityEngine.Object" && name == "op_Equality") return Own(nameof(Equal));
        if (type == "SeaPower.ObjectBase" && name == "isUnit") return Method(typeof(UnitObject), nameof(UnitObject.IsUnit));
        if (type == "SeaPower.ObjectBase" && name == "isSubmerged") return Method(typeof(UnitObject), nameof(UnitObject.IsSubmerged));
        if (type == "SeaPower.ObjectBase" && name == "get__taskforce") return Getter(typeof(UnitObject), nameof(UnitObject.Force));
        if (type == "SeaPower.ObjectBase" && name == "get_VisibleAltitude") return Getter(typeof(UnitObject), nameof(UnitObject.VisibleAltitude));
        if (type == "SeaPower.TerrainUtils" && name == "IsLineOfSightBetween") return Own(nameof(LineOfSight));
        if (type == "SeaPower.RadarCalculator" && name == "GetCommPosition") return Own(nameof(CommPosition));
        if (type == "SeaPower.RadarCalculator" && name == "getRadarHorizon") return Own(nameof(Horizon));
        if (type == "SeaPower.Utils" && name == "TerrainAltForHorizon") return Own(nameof(TerrainAltitude));
        if (type == "SeaPower.Utils" && name == "RevealContactToObject") return Own(nameof(Reveal));
        if (type == "UnityEngine.Component" && name == "get_transform") return Getter(typeof(UnitObject), nameof(UnitObject.Transform));
        if (type == "UnityEngine.Transform" && name == "get_position") return Getter(typeof(Transform), nameof(Transform.Position));
        if (type == "UnityEngine.Vector3" && name == "op_Subtraction") return Own(nameof(Subtract));
        if (type == "UnityEngine.Vector3" && name == "get_magnitude") return Getter(typeof(Vector), nameof(Vector.Magnitude));
        if (type == "SeaPower.LanguageResourceHandler" && name == "getText") return Method(typeof(Language), nameof(Language.GetText));
        if (type == "SeaPower.AI" && name == "get_CurrentCompliance") return Getter(typeof(Brain), nameof(Brain.Compliance));
        if (type == "SeapowerUI.ViewModels.MainGameViewModel" && name == "get_Map") return Getter(typeof(Game), nameof(Game.Map));
        if (type == "SeapowerUI.ViewModels.MainGameViewModel" && name == "RaiseAlert") return Method(typeof(Game), nameof(Game.RaiseAlert));
        if (type.StartsWith("UniRx.ReactiveProperty`1", StringComparison.Ordinal) && name == "get_Value")
            return method.DeclaringType!.GetGenericArguments()[0] == typeof(bool)
                ? Getter(typeof(Identification), nameof(Identification.Value))
                : Getter(typeof(Selection), nameof(Selection.Value));
        throw new NotSupportedException("Unmapped game method: " + method);
    }
}
