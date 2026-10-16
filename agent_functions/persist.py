from mythic_container.MythicCommandBase import *

class PersistArguments(TaskArguments):
    def __init__(self, command_line, **kwargs):
        super().__init__(command_line, **kwargs)
        self.args = [
            CommandParameter(
                name="action",
                type=ParameterType.ChooseOne,
                choices=["install", "remove"],
                description="Install or remove persistence",
                default_value="install",
                parameter_group_info=[ParameterGroupInfo(required=False)]
            )
        ]

    async def parse_arguments(self):
        if self.command_line.strip().lower() == "remove":
            self.add_arg("action", "remove")
        else:
            self.add_arg("action", "install")

class PersistCommand(CommandBase):
    cmd = "persist"
    needs_admin = False
    help_cmd = "persist [remove]"
    description = "Copy agent to %APPDATA% and set HKCU Run key for persistence. Pass 'remove' to clean up."
    version = 1
    author = "lab"
    argument_class = PersistArguments
    attackmapping = ["T1547.001"]

    async def create_go_tasking(self, taskData: PTTaskMessageAllData) -> PTTaskCreateTaskingMessageResponse:
        action = taskData.args.get_arg("action")
        return PTTaskCreateTaskingMessageResponse(
            TaskID=taskData.Task.ID,
            Success=True,
            DisplayParams=action
        )

    async def process_response(self, task: PTTaskMessageAllData, response: any) -> PTTaskProcessResponseMessageResponse:
        return PTTaskProcessResponseMessageResponse(TaskID=task.Task.ID, Success=True)
