using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.HIT.TaskTemplate;

class Main
{
    /// <summary>
    /// This is the information shown about the task in the Frends control panel.
    /// The TaskInput input parameters will be shown on a different tab than the verbose option with the PropertyTab attribute.
    /// </summary> 
    /// <param name="verbose">This parameter increases the verbosity of the output</param>
    /// <param name="input">A set of parameters for doing something in the class</param>
    /// <returns>TaskOutput object</returns>
    public static TaskOutput DoSomething(bool verbose, [PropertyTab] TaskInput input)
    {
        bool isSuccess = Helpers.IsSuccessful();
        string message = "This works!";

        return new TaskOutput(
            success: isSuccess,
            info: message
        );
    }
}