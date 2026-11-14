using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blazejewicz.A2Ui.Core.Bindings;

/// <summary>
/// Union type representing a value that can be a literal (string, number, boolean, array, object),
/// a data binding path, or a function call. Maps to DynamicValue in common_types.json.
/// </summary>
[JsonConverter(typeof(DynamicValueConverter))]
public abstract record DynamicValue
{
    private DynamicValue() { }

    /// <summary>A string literal value.</summary>
    /// <param name="Value">The string literal value.</param>
    public sealed record StringValue(string Value) : DynamicValue
    {
        /// <inheritdoc />
        public override T Match<T>(
            Func<StringValue, T> onString,
            Func<NumberValue, T> onNumber,
            Func<BoolValue, T> onBool,
            Func<ArrayValue, T> onArray,
            Func<PathValue, T> onPath,
            Func<FunctionValue, T> onFunction,
            Func<ObjectValue, T> onObject
        ) => onString(this);

        /// <inheritdoc />
        public override void Switch(
            Action<StringValue> onString,
            Action<NumberValue> onNumber,
            Action<BoolValue> onBool,
            Action<ArrayValue> onArray,
            Action<PathValue> onPath,
            Action<FunctionValue> onFunction,
            Action<ObjectValue> onObject
        ) => onString(this);
    }

    /// <summary>A numeric literal value.</summary>
    /// <param name="Value">The numeric literal value.</param>
    public sealed record NumberValue(double Value) : DynamicValue
    {
        /// <inheritdoc />
        public override T Match<T>(
            Func<StringValue, T> onString,
            Func<NumberValue, T> onNumber,
            Func<BoolValue, T> onBool,
            Func<ArrayValue, T> onArray,
            Func<PathValue, T> onPath,
            Func<FunctionValue, T> onFunction,
            Func<ObjectValue, T> onObject
        ) => onNumber(this);

        /// <inheritdoc />
        public override void Switch(
            Action<StringValue> onString,
            Action<NumberValue> onNumber,
            Action<BoolValue> onBool,
            Action<ArrayValue> onArray,
            Action<PathValue> onPath,
            Action<FunctionValue> onFunction,
            Action<ObjectValue> onObject
        ) => onNumber(this);
    }

    /// <summary>A boolean literal value.</summary>
    /// <param name="Value">The boolean literal value.</param>
    public sealed record BoolValue(bool Value) : DynamicValue
    {
        /// <inheritdoc />
        public override T Match<T>(
            Func<StringValue, T> onString,
            Func<NumberValue, T> onNumber,
            Func<BoolValue, T> onBool,
            Func<ArrayValue, T> onArray,
            Func<PathValue, T> onPath,
            Func<FunctionValue, T> onFunction,
            Func<ObjectValue, T> onObject
        ) => onBool(this);

        /// <inheritdoc />
        public override void Switch(
            Action<StringValue> onString,
            Action<NumberValue> onNumber,
            Action<BoolValue> onBool,
            Action<ArrayValue> onArray,
            Action<PathValue> onPath,
            Action<FunctionValue> onFunction,
            Action<ObjectValue> onObject
        ) => onBool(this);
    }

    /// <summary>A JSON array literal value.</summary>
    /// <param name="Value">The JSON array element value.</param>
    public sealed record ArrayValue(JsonElement Value) : DynamicValue
    {
        /// <inheritdoc />
        public override T Match<T>(
            Func<StringValue, T> onString,
            Func<NumberValue, T> onNumber,
            Func<BoolValue, T> onBool,
            Func<ArrayValue, T> onArray,
            Func<PathValue, T> onPath,
            Func<FunctionValue, T> onFunction,
            Func<ObjectValue, T> onObject
        ) => onArray(this);

        /// <inheritdoc />
        public override void Switch(
            Action<StringValue> onString,
            Action<NumberValue> onNumber,
            Action<BoolValue> onBool,
            Action<ArrayValue> onArray,
            Action<PathValue> onPath,
            Action<FunctionValue> onFunction,
            Action<ObjectValue> onObject
        ) => onArray(this);
    }

    /// <summary>A data binding path reference (e.g. "/user/name" or "@path").</summary>
    /// <param name="DataPath">The JSON Pointer path string.</param>
    public sealed record PathValue(string DataPath) : DynamicValue
    {
        /// <inheritdoc />
        public override T Match<T>(
            Func<StringValue, T> onString,
            Func<NumberValue, T> onNumber,
            Func<BoolValue, T> onBool,
            Func<ArrayValue, T> onArray,
            Func<PathValue, T> onPath,
            Func<FunctionValue, T> onFunction,
            Func<ObjectValue, T> onObject
        ) => onPath(this);

        /// <inheritdoc />
        public override void Switch(
            Action<StringValue> onString,
            Action<NumberValue> onNumber,
            Action<BoolValue> onBool,
            Action<ArrayValue> onArray,
            Action<PathValue> onPath,
            Action<FunctionValue> onFunction,
            Action<ObjectValue> onObject
        ) => onPath(this);
    }

    /// <summary>A client-side or agent function call.</summary>
    /// <param name="Call">The function call details.</param>
    public sealed record FunctionValue(FunctionCallValue Call) : DynamicValue
    {
        /// <inheritdoc />
        public override T Match<T>(
            Func<StringValue, T> onString,
            Func<NumberValue, T> onNumber,
            Func<BoolValue, T> onBool,
            Func<ArrayValue, T> onArray,
            Func<PathValue, T> onPath,
            Func<FunctionValue, T> onFunction,
            Func<ObjectValue, T> onObject
        ) => onFunction(this);

        /// <inheritdoc />
        public override void Switch(
            Action<StringValue> onString,
            Action<NumberValue> onNumber,
            Action<BoolValue> onBool,
            Action<ArrayValue> onArray,
            Action<PathValue> onPath,
            Action<FunctionValue> onFunction,
            Action<ObjectValue> onObject
        ) => onFunction(this);
    }

    /// <summary>A plain JSON object literal value.</summary>
    /// <param name="Value">The object key-value dictionary.</param>
    public sealed record ObjectValue(IReadOnlyDictionary<string, object?> Value) : DynamicValue
    {
        /// <inheritdoc />
        public override T Match<T>(
            Func<StringValue, T> onString,
            Func<NumberValue, T> onNumber,
            Func<BoolValue, T> onBool,
            Func<ArrayValue, T> onArray,
            Func<PathValue, T> onPath,
            Func<FunctionValue, T> onFunction,
            Func<ObjectValue, T> onObject
        ) => onObject(this);

        /// <inheritdoc />
        public override void Switch(
            Action<StringValue> onString,
            Action<NumberValue> onNumber,
            Action<BoolValue> onBool,
            Action<ArrayValue> onArray,
            Action<PathValue> onPath,
            Action<FunctionValue> onFunction,
            Action<ObjectValue> onObject
        ) => onObject(this);
    }

    // ── Inspection properties ────────────────────────────────────────

    /// <summary>Returns true if this value is a <see cref="StringValue"/>.</summary>
    public bool IsString => this is StringValue;

    /// <summary>Returns true if this value is a <see cref="NumberValue"/>.</summary>
    public bool IsNumber => this is NumberValue;

    /// <summary>Returns true if this value is a <see cref="BoolValue"/>.</summary>
    public bool IsBool => this is BoolValue;

    /// <summary>Returns true if this value is an <see cref="ArrayValue"/>.</summary>
    public bool IsArray => this is ArrayValue;

    /// <summary>Returns true if this value is a <see cref="PathValue"/>.</summary>
    public bool IsPath => this is PathValue;

    /// <summary>Returns true if this value is a <see cref="FunctionValue"/>.</summary>
    public bool IsFunction => this is FunctionValue;

    /// <summary>Returns true if this value is an <see cref="ObjectValue"/>.</summary>
    public bool IsObject => this is ObjectValue;

    /// <summary>Casts to <see cref="StringValue"/> or returns null.</summary>
    public StringValue? AsString => this as StringValue;

    /// <summary>Casts to <see cref="NumberValue"/> or returns null.</summary>
    public NumberValue? AsNumber => this as NumberValue;

    /// <summary>Casts to <see cref="BoolValue"/> or returns null.</summary>
    public BoolValue? AsBool => this as BoolValue;

    /// <summary>Casts to <see cref="ArrayValue"/> or returns null.</summary>
    public ArrayValue? AsArray => this as ArrayValue;

    /// <summary>Casts to <see cref="PathValue"/> or returns null.</summary>
    public PathValue? AsPath => this as PathValue;

    /// <summary>Casts to <see cref="FunctionValue"/> or returns null.</summary>
    public FunctionValue? AsFunction => this as FunctionValue;

    /// <summary>Casts to <see cref="ObjectValue"/> or returns null.</summary>
    public ObjectValue? AsObject => this as ObjectValue;

    // ── Pattern matching ─────────────────────────────────────────────

    /// <summary>Pattern matches exhaustively over all 7 union cases.</summary>
    public abstract T Match<T>(
        Func<StringValue, T> onString,
        Func<NumberValue, T> onNumber,
        Func<BoolValue, T> onBool,
        Func<ArrayValue, T> onArray,
        Func<PathValue, T> onPath,
        Func<FunctionValue, T> onFunction,
        Func<ObjectValue, T> onObject
    );

    /// <summary>Pattern matches over the 6 primitive, array, path, and function union cases.</summary>
    [Obsolete("Use the 7-parameter overload that includes onObject.")]
    public virtual T Match<T>(
        Func<StringValue, T> onString,
        Func<NumberValue, T> onNumber,
        Func<BoolValue, T> onBool,
        Func<ArrayValue, T> onArray,
        Func<PathValue, T> onPath,
        Func<FunctionValue, T> onFunction
    ) => this.Match(onString, onNumber, onBool, onArray, onPath, onFunction, _ => default!);

    /// <summary>Executes side-effects exhaustively over all 7 union cases.</summary>
    public abstract void Switch(
        Action<StringValue> onString,
        Action<NumberValue> onNumber,
        Action<BoolValue> onBool,
        Action<ArrayValue> onArray,
        Action<PathValue> onPath,
        Action<FunctionValue> onFunction,
        Action<ObjectValue> onObject
    );

    /// <summary>Executes side-effects over the 6 primitive, array, path, and function union cases.</summary>
    [Obsolete("Use the 7-parameter overload that includes onObject.")]
    public virtual void Switch(
        Action<StringValue> onString,
        Action<NumberValue> onNumber,
        Action<BoolValue> onBool,
        Action<ArrayValue> onArray,
        Action<PathValue> onPath,
        Action<FunctionValue> onFunction
    ) => this.Switch(onString, onNumber, onBool, onArray, onPath, onFunction, _ => { });

    // ── Factory methods ──────────────────────────────────────────────

    /// <summary>Creates a string value.</summary>
    public static DynamicValue FromString(string value) => new StringValue(value);

    /// <summary>Creates a data binding path value.</summary>
    public static DynamicValue FromPath(string path) => new PathValue(path);

    /// <summary>Creates a numeric value.</summary>
    public static DynamicValue FromNumber(double value) => new NumberValue(value);

    /// <summary>Creates a boolean value.</summary>
    public static DynamicValue FromBool(bool value) => new BoolValue(value);

    /// <summary>Creates a function call value.</summary>
    public static DynamicValue FromFunctionCall(FunctionCallValue call) => new FunctionValue(call);

    /// <summary>Creates an object literal value.</summary>
    public static DynamicValue FromObject(IReadOnlyDictionary<string, object?> value) => new ObjectValue(value);
}
