from mythic_container.MythicCommandBase import *

class UploadArguments(TaskArguments):
    def __init__(self, command_line, **kwargs):
        super().__init__(command_line, **kwargs)
        self.args = [
            CommandParameter(
                name="remote_path",
                type=ParameterType.String,
                description="Full path of file on target to upload to Mythic",
                parameter_group_info=[ParameterGroupInfo(required=True)]
            )
        ]

    async def parse_arguments(self):
        if len(self.command_line) > 0:
            self.add_arg("remote_path", self.command_line)

class UploadCommand(CommandBase):
    cmd = "upload"
    needs_admin = False
    help_cmd = "upload <remote_path>"
    description = "Upload a file from the agent to Mythic"
    version = 1
    author = "lab"
    argument_class = UploadArguments
    attackmapping = ["T1041"]

    async def create_go_tasking(self, taskData: PTTaskMessageAllData) -> PTTaskCreateTaskingMessageResponse:
        resp = PTTaskCreateTaskingMessageResponse(
            TaskID=taskData.Task.ID,
            Success=True,
            DisplayParams=taskData.args.get_arg("remote_path")
        )
        return resp

    async def process_response(self, task: PTTaskMessageAllData, response: any) -> PTTaskProcessResponseMessageResponse:
        resp = PTTaskProcessResponseMessageResponse(TaskID=task.Task.ID, Success=True)
        return resp
