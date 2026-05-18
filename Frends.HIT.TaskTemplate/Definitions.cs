using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

// This should be the root namespace of the package
namespace Frends.HIT.TaskTemplate;

public enum MultipleChoiceOption
{
    /// <summary>
    /// This is the text that is shown in the control panel when hovering over the option
    /// </summary>
    [Display(Name = "Option A Display Name")]
    OptionA,
    
    /// <summary>
    /// This is the text that is shown in the control panel when hovering over the option
    /// </summary>
    [Display(Name = "Option B Display Name")]
    OptionB,
    
    /// <summary>
    /// This is the text that is shown in the control panel when hovering over the option
    /// </summary>
    [Display(Name = "Option C Display Name")]
    OptionC,
}

/// <summary>
/// This is the information shown about the input group in the control panel
/// Since this is set via the interface and not programmatically, it doesn't need a public class for instantiating.
/// </summary>
public class TaskInput
{
    /// <summary>
    /// This is the information shown about the parameter in the control panel
    /// </summary>
    [Display(Name = "Multiple Choice Option Name")]
    public MultipleChoiceOption ChoiceOption { get; set; }
    
    /// <summary>
    /// This is the information shown about the text parameter in the panel
    /// If no text is provided, the default value is "ABCDEFG" (also shown in control panel)
    /// </summary>
    [DefaultValue("ABCDEFG")]
    [Display(Name = "Text Parameter Name")]
    public string NameOption { get; set; }
    
    /// <summary>
    /// The information shown about this parameter in the panel.
    /// With the PasswordPropertyText attribute, the content is hidden by default
    /// </summary>
    [PasswordPropertyText]
    public string PasswordOption { get; set; }
    
    /// <summary>
    /// The default editor box for this parameter will be of the "Expression" type
    /// </summary>
    [DisplayFormat(DataFormatString = "Expression")]
    public string ExpressionTextOption { get; set; }
    
    /// <summary>
    /// A boolean option
    /// </summary>
    public bool BooleanOption { get; set; }
    
    /// <summary>
    /// This field will only be shown if the "ChoiceOption" field is A
    /// </summary>
    [UIHint(nameof(ChoiceOption), "", MultipleChoiceOption.OptionA, MultipleChoiceOption.OptionB)]
    public string HiddenTextOption { get; set; }
    
    /// <summary>
    /// This field will only be shown if the BooleanOption field is false
    /// </summary>
    [UIHint(nameof(BooleanOption), "", false)]
    public string HiddenBooleanOption { get; set; }
}

/// <summary>
/// This is the output from the task.
/// Since this will be defined programmatically, there is an upside to using
/// a public facing class for creating an instance for the output. 
/// </summary>
public class TaskOutput
{
    /// <summary>
    /// Whether the operation was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Any messages returned by the task
    /// </summary>
    public string Info { get; set; }

    public TaskOutput(
        bool success,
        string info
    )
    {
        Success = success;
        Info = info;
    }
}