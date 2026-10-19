// DataGridView column with NumericUpDown spinners (limit editing with up/down arrows).
// Self-contained replacement for the original DataGridViewNumericUpDownElements.dll.
// Whole numbers only: no decimals, no thousands separators (clean copy/paste).

namespace SelfishNetV2.Test.UI;

public sealed class NumericUpDownColumn : DataGridViewColumn
{
    public NumericUpDownColumn() : base(new NumericUpDownCell()) { }

    public decimal Minimum { get; set; } = 0m;
    public decimal Maximum { get; set; } = 1_000_000m;
    public decimal Increment { get; set; } = 1m;
    public int DecimalPlaces { get; set; } = 0;

    public override DataGridViewCell CellTemplate
    {
        get => base.CellTemplate;
        set
        {
            if (value is not null and not NumericUpDownCell)
                throw new InvalidCastException("CellTemplate must be a NumericUpDownCell.");
            base.CellTemplate = value;
        }
    }
}

public sealed class NumericUpDownCell : DataGridViewTextBoxCell
{
    public override Type EditType => typeof(NumericUpDownEditingControl);
    public override Type ValueType => typeof(decimal);
    public override object DefaultNewRowValue => 0m;

    public override void InitializeEditingControl(int rowIndex, object? initialFormattedValue,
        DataGridViewCellStyle dataGridViewCellStyle)
    {
        base.InitializeEditingControl(rowIndex, initialFormattedValue, dataGridViewCellStyle);
        if (DataGridView?.EditingControl is NumericUpDownEditingControl ctl && OwningColumn is NumericUpDownColumn col)
        {
            ctl.Minimum = col.Minimum;
            ctl.Maximum = col.Maximum;
            ctl.Increment = col.Increment;
            ctl.DecimalPlaces = col.DecimalPlaces;
            decimal v = 0m;
            try { v = Convert.ToDecimal(Value ?? 0m); } catch { /* keep 0 */ }
            ctl.Value = Math.Min(col.Maximum, Math.Max(col.Minimum, v));
        }
    }
}

internal sealed class NumericUpDownEditingControl : NumericUpDown, IDataGridViewEditingControl
{
    private DataGridView? _grid;
    private bool _valueChanged;
    private int _rowIndex;

    public NumericUpDownEditingControl()
    {
        Minimum = 0;
        Maximum = 1_000_000;
        Increment = 1;
        DecimalPlaces = 0;
        ThousandsSeparator = false;
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        // Blank when zero (just type), select-all when non-zero (typing replaces).
        if (Value == 0)
            Text = "";
        else
            Select(0, Text.Length);
    }

    protected override void OnLeave(EventArgs e)
    {
        // A blank box means 0 (unlimited).
        if (string.IsNullOrWhiteSpace(Text))
            Value = Minimum;
        base.OnLeave(e);
    }

    /// <summary>Pushes typed (not yet validated) text into Value. Returns the value.</summary>
    public decimal FlushText()
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            Value = Minimum;
        }
        else if (decimal.TryParse(Text, out var d))
        {
            Value = Math.Min(Maximum, Math.Max(Minimum, d));
        }
        return Value;
    }

    public object EditingControlFormattedValue
    {
        get => Value.ToString();
        set
        {
            if (value is string s && decimal.TryParse(s, out var d))
                Value = Math.Min(Maximum, Math.Max(Minimum, d));
        }
    }

    public object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context) =>
        EditingControlFormattedValue;

    public void ApplyCellStyleToEditingControl(DataGridViewCellStyle dataGridViewCellStyle)
    {
        Font = dataGridViewCellStyle.Font ?? Font;
        ForeColor = dataGridViewCellStyle.ForeColor;
        BackColor = dataGridViewCellStyle.BackColor;
    }

    public int EditingControlRowIndex { get => _rowIndex; set => _rowIndex = value; }
    public bool EditingControlWantsInputKey(Keys key, bool dataGridViewWantsInputKey) => key switch
    {
        Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home or Keys.End
            or Keys.PageDown or Keys.PageUp => true,
        _ => !dataGridViewWantsInputKey,
    };
    public void PrepareEditingControlForEdit(bool selectAll)
    {
        if (selectAll) Select(0, Text.Length);
    }
    public bool RepositionEditingControlOnValueChange => false;
    public Cursor EditingPanelCursor => Cursors.Default;
    public DataGridView? EditingControlDataGridView { get => _grid; set => _grid = value; }
    public bool EditingControlValueChanged { get => _valueChanged; set => _valueChanged = value; }

    protected override void OnValueChanged(EventArgs e)
    {
        _valueChanged = true;
        _grid?.NotifyCurrentCellDirty(true);
        base.OnValueChanged(e);
    }
}
