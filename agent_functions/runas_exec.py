from mythic_container.MythicCommandBase import *

class RunasExecArguments(TaskArguments):
    def __init__(self, command_line, **kwargs):
        super().__init__(command_line, **kwargs)
        self.args = [
            CommandParameter(
                name="command",
                type=ParameterType.String,
                description="Command to run with runas verb (triggers UAC elevation prompt)",
                parameter_group_info=[ParameterGroupInfo(required=True)]
            )
        ]

    async def parse_arguments(self):
        if len(self.command_line) > 0:
            self.add_arg("command", self.command_line)

class RunasExecCommand(CommandBase):
    cmd = "runas_exec"
    needs_admin = False
    help_cmd = "runas_exec <command>"
    description = (
        "Run a command using the runas shell verb (Process.Start with Verb=runas). "
        "Triggers a UAC elevation prompt on the target desktop. "
        "Use to study UAC prompt behaviour and Sysmon EID 1 artefacts."
    )
    version = 1
    author = "lab"
    argument_class = RunasExecArguments
    attackmapping = ["T1548.002"]

    async def create_go_tasking(self, taskData: PTTaskMessageAllData) -> PTTaskCreateTaskingMessageResponse:
        return PTTaskCreateTaskingMessageResponse(
            TaskID=taskData.Task.ID,
            Success=True,
            DisplayParams=taskData.args.get_arg("command")
        )

    async def process_response(self, task: PTTaskMessageAllData, response: any) -> PTTaskProcessResponseMessageResponse:
        return PTTaskProcessResponseMessageResponse(TaskID=task.Task.ID, Success=True)
