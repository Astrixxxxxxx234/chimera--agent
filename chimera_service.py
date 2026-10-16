import mythic_container
import asyncio
from chimera import chimera_agent

async def main():
    await mythic_container.mythic_service.start_and_run_forever()

asyncio.run(main())
