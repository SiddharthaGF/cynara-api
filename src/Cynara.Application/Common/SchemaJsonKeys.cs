namespace Cynara.Application.Common;

public static class SchemaJsonKeys
{
    public const string Fields = "fields";
    public const string SchemaVersion = "schemaVersion";
    public const string ClinicalSchemaVersion = "clinicalSchemaVersion";
    public const string Schema = "$schema";
    public const string Layout = "layout";
    public const string Validations = "validations";
    public const string FieldId = "fieldId";
    public const string Items = "items";
    public const string Children = "children";
    public const string Label = "label";
    public const string Widget = "widget";
    public const string Width = "width";
    public const string Id = "id";
    public const string Type = "type";
    public const string Code = "code";
    public const string Options = "options";
    public const string Calculate = "calculate";
    public const string Message = "message";
}

public static class FieldTypeNames
{
    public const string Text = "text";
    public const string Textarea = "textarea";
    public const string Number = "number";

#pragma warning disable CA1720 // Identifier contains type name
    public const string Integer = "integer";
#pragma warning restore CA1720 // Identifier contains type name
    public const string Boolean = "boolean";
    public const string Date = "date";
    public const string DateTime = "datetime";
    public const string Time = "time";
    public const string Choice = "choice";
    public const string Group = "group";
    public const string Repeater = "repeater";
    public const string ComponentRef = "component-ref";
}

public static class WidgetNames
{
    public const string TextInput = "text-input";
    public const string Textarea = "textarea";
    public const string NumberInput = "number-input";
    public const string IntegerInput = "integer-input";
    public const string Toggle = "toggle";
    public const string DatePicker = "date-picker";
    public const string DateTimePicker = "datetime-picker";
    public const string TimePicker = "time-picker";
    public const string Select = "select";
    public const string Group = "group";
    public const string Repeater = "repeater";
}

public static class AuditEntityTypes
{
    public const string FormVersion = "form-version";
    public const string FormResponse = "form-response";
    public const string ComponentVersion = "component-version";
    public const string Hospital = "hospital";
    public const string Facility = "facility";
    public const string ClinicalArea = "clinical-area";
    public const string Discipline = "discipline";
    public const string DocumentDefinition = "document-definition";
    public const string ClinicalDocument = "clinical-document";
    public const string Patient = "patient";
    public const string Encounter = "encounter";
}
