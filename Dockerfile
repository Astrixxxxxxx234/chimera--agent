FROM debian:bookworm-slim

RUN apt-get update && apt-get install -y --no-install-recommends \
    mono-mcs \
    python3 \
    python3-pip \
    zip \
    wget \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

RUN pip3 install --break-system-packages mythic-container

WORKDIR /Chimera

COPY . .

RUN pip3 install --break-system-packages -r requirements.txt

CMD ["python3", "chimera_service.py"]
