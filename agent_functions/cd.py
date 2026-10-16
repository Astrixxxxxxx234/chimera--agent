from mythic_container.MythicCommandBase import *

class CdArguments(TaskArguments):
    def __init__(self, command_line, **kwargs):
        super().__init__(command_line, **kwargs)
        self.args = [
            CommandParameter(
                name="path",
                type=ParameterType.String,
                description="Directory to change to",
                parameter_group_info=[ParameterGroupInfo(required=True)]
            )
        ]

    async def parse_arguments(self):
        if len(self.command_line) > 0:
            self.add_arg("path", self.command_line)

class CdCommand(CommandBase):
    cmd = "cd"
    needs_admin = False
    help_cmd = "cd <path>"
    description = "Change current working directory"
    version = 1
    author = "lab"
    argument_class = CdArguments
    attackmapping = ["T1083"]

    async def create_go_tasking(self, taskData: PTTaskMessageAllData) -> PTTaskCreateTaskingMessageResponse:
        resp = PTTaskCreateTaskingMessageResponse(
            TaskID=taskData.Task.ID,
            Success=True,
            DisplayParams=taskData.args.get_arg("path")
        )
        return resp

    async def process_response(self, task: PTTaskMessageAllData, response: any) -> PTTaskProcessResponseMessageResponse:
        resp = PTTaskProcessResponseMessageResponse(TaskID=task.Task.ID, Success=True)
        return resp
