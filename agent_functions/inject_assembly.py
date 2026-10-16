from mythic_container.MythicCommandBase import *

class InjectAssemblyArguments(TaskArguments):
    def __init__(self, command_line, **kwargs):
        super().__init__(command_line, **kwargs)
        self.args = [
            CommandParameter(
                name="assembly_b64",
                type=ParameterType.String,
                description="Base64-encoded .NET DLL to load in-memory and execute via Run()",
                parameter_group_info=[ParameterGroupInfo(required=True)]
            )
        ]

    async def parse_arguments(self):
        if len(self.command_line) > 0:
            self.add_arg("assembly_b64", self.command_line.strip())

class InjectAssemblyCommand(CommandBase):
    cmd = "inject_assembly"
    needs_admin = False
    help_cmd = "inject_assembly <base64_dll>"
    description = (
        "Load a base64-encoded .NET DLL into memory via Assembly.Load() and call its "
        "public static Run() method. DLL never touches disk. "
        "Use with your own test DLL to study reflective in-memory execution."
    )
    version = 1
    author = "lab"
    argument_class = InjectAssemblyArguments
    attackmapping = ["T1620"]

    async def create_go_tasking(self, taskData: PTTaskMessageAllData) -> PTTaskCreateTaskingMessageResponse:
        return PTTaskCreateTaskingMessageResponse(
            TaskID=taskData.Task.ID,
            Success=True,
            DisplayParams="<assembly blob>"
        )

    async def process_response(self, task: PTTaskMessageAllData, response: any) -> PTTaskProcessResponseMessageResponse:
        return PTTaskProcessResponseMessageResponse(TaskID=task.Task.ID, Success=True)
