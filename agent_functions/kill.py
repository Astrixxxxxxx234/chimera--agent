from mythic_container.MythicCommandBase import *

class KillArguments(TaskArguments):
    def __init__(self, command_line, **kwargs):
        super().__init__(command_line, **kwargs)
        self.args = []

    async def parse_arguments(self):
        pass

class KillCommand(CommandBase):
    cmd = "kill"
    needs_admin = False
    help_cmd = "kill"
    description = "Terminate the agent process"
    version = 1
    author = "lab"
    argument_class = KillArguments
    attackmapping = []

    async def create_go_tasking(self, taskData: PTTaskMessageAllData) -> PTTaskCreateTaskingMessageResponse:
        resp = PTTaskCreateTaskingMessageResponse(
            TaskID=taskData.Task.ID,
            Success=True,
            DisplayParams=""
        )
        return resp

    async def process_response(self, task: PTTaskMessageAllData, response: any) -> PTTaskProcessResponseMessageResponse:
        resp = PTTaskProcessResponseMessageResponse(TaskID=task.Task.ID, Success=True)
        return resp
