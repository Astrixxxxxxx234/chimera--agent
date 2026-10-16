from mythic_container.MythicCommandBase import *

class ShellArguments(TaskArguments):
    def __init__(self, command_line, **kwargs):
        super().__init__(command_line, **kwargs)
        self.args = [
            CommandParameter(
                name="command",
                type=ParameterType.String,
                description="Command to execute via cmd /c",
                parameter_group_info=[ParameterGroupInfo(required=True)]
            )
        ]

    async def parse_arguments(self):
        if len(self.command_line) > 0:
            self.add_arg("command", self.command_line)

class ShellCommand(CommandBase):
    cmd = "shell"
    needs_admin = False
    help_cmd = "shell <command>"
    description = "Execute a command via cmd /c and return output"
    version = 1
    author = "lab"
    argument_class = ShellArguments
    attackmapping = ["T1059.003"]

    async def create_go_tasking(self, taskData: PTTaskMessageAllData) -> PTTaskCreateTaskingMessageResponse:
        resp = PTTaskCreateTaskingMessageResponse(
            TaskID=taskData.Task.ID,
            Success=True,
            DisplayParams=taskData.args.get_arg("command")
        )
        return resp

    async def process_response(self, task: PTTaskMessageAllData, response: any) -> PTTaskProcessResponseMessageResponse:
        resp = PTTaskProcessResponseMessageResponse(TaskID=task.Task.ID, Success=True)
        return resp
