FROM mono:latest

RUN apt-get update && apt-get install -y \
    mono-mcs python3 python3-pip zip wget \
    && pip3 install --break-system-packages mythic-container

WORKDIR /Chimera

COPY . .

RUN pip3 install --break-system-packages -r requirements.txt

CMD ["python3", "chimera_service.py"]
