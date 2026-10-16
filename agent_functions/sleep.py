from mythic_container.MythicCommandBase import *

class SleepArguments(TaskArguments):
    def __init__(self, command_line, **kwargs):
        super().__init__(command_line, **kwargs)
        self.args = [
            CommandParameter(
                name="interval",
                type=ParameterType.Number,
                description="New sleep interval in seconds",
                parameter_group_info=[ParameterGroupInfo(required=True)]
            ),
            CommandParameter(
                name="jitter",
                type=ParameterType.Number,
                description="Jitter percentage (0-100)",
                default_value=30,
                parameter_group_info=[ParameterGroupInfo(required=False)]
            )
        ]

    async def parse_arguments(self):
        self.load_args_from_json_string(self.command_line)

class SleepCommand(CommandBase):
    cmd = "sleep"
    needs_admin = False
    help_cmd = "sleep <interval> [jitter]"
    description = "Change agent beacon interval and jitter"
    version = 1
    author = "lab"
    argument_class = SleepArguments
    attackmapping = []

    async def create_go_tasking(self, taskData: PTTaskMessageAllData) -> PTTaskCreateTaskingMessageResponse:
        resp = PTTaskCreateTaskingMessageResponse(
            TaskID=taskData.Task.ID,
            Success=True,
            DisplayParams=f"interval={taskData.args.get_arg('interval')} jitter={taskData.args.get_arg('jitter')}%"
        )
        return resp

    async def process_response(self, task: PTTaskMessageAllData, response: any) -> PTTaskProcessResponseMessageResponse:
        resp = PTTaskProcessResponseMessageResponse(TaskID=task.Task.ID, Success=True)
        return resp
