from mythic_container.MythicCommandBase import *

class DownloadArguments(TaskArguments):
    def __init__(self, command_line, **kwargs):
        super().__init__(command_line, **kwargs)
        self.args = [
            CommandParameter(
                name="remote_path",
                type=ParameterType.String,
                description="Full path to write file on target",
                parameter_group_info=[ParameterGroupInfo(required=True)]
            ),
            CommandParameter(
                name="file",
                type=ParameterType.File,
                description="File to download to the agent",
                parameter_group_info=[ParameterGroupInfo(required=True)]
            )
        ]

    async def parse_arguments(self):
        self.load_args_from_json_string(self.command_line)

class DownloadCommand(CommandBase):
    cmd = "download"
    needs_admin = False
    help_cmd = "download"
    description = "Download a file from Mythic to the agent"
    version = 1
    author = "lab"
    argument_class = DownloadArguments
    attackmapping = ["T1105"]

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
