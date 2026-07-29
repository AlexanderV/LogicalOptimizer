# Roadmap P0.2 — one controlled Linux environment for the cross-library comparison.
#
# The methodology (doc/COMPARISON_METHODOLOGY.md) requires a SINGLE runner with one CPU
# policy and one identical timeout for every tool. This image bundles the OUR side (.NET
# SDK) with every competitor — SymPy, PyEDA, CaDiCaL, Kissat, d4, and LogicNG — so the
# whole comparison runs in one place from one committed corpus.
#
# Every competitor is installed BEST-EFFORT: a tool whose build fails does not fail the
# image, it is simply absent and its adapter self-skips (the cell stays `pending`). No
# number is ever fabricated.
#
# Build (context = tools/comparison):   docker build -t logicopt-p0p2 tools/comparison
# Run   (repo mounted at /work):        docker run --rm -v <repo>:/work logicopt-p0p2
#
# All competitor versions are PINNED for reproducibility: the base image by digest, the
# Python tools by exact version, and the SAT/# SAT solvers by commit hash.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664

ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential git cmake rsync bc ca-certificates \
        python3 python3-pip python3-venv python3-dev \
        default-jdk maven \
        libgmp-dev zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

# --- Python competitors (isolated venv; Debian's system python is externally-managed) ---
RUN python3 -m venv /opt/venv
ENV PATH="/opt/venv/bin:${PATH}"
RUN pip install --no-cache-dir sympy==1.14.0 || echo "WARN: sympy install failed (column self-skips)"
RUN pip install --no-cache-dir pyeda==0.29.0 || echo "WARN: pyeda install failed (column self-skips)"
RUN pip install --no-cache-dir 'z3-solver==4.16.0.0' || echo "WARN: z3-solver install failed (column self-skips)"

# --- CaDiCaL (SAT), pinned commit ---
RUN git clone https://github.com/arminbiere/cadical /tmp/cadical \
        && cd /tmp/cadical && git checkout c60730422e758ef1cebe7aeddf2dda31c996bf04 \
        && ./configure && make -j"$(nproc)" \
        && cp build/cadical /usr/local/bin/ \
        && rm -rf /tmp/cadical \
        || echo "WARN: cadical build failed (column self-skips)"

# --- Kissat (SAT), pinned commit ---
RUN git clone https://github.com/arminbiere/kissat /tmp/kissat \
        && cd /tmp/kissat && git checkout 8af8e56f174b778aef3aa45af9f739b2a5f492c2 \
        && ./configure && make -j"$(nproc)" \
        && cp build/kissat /usr/local/bin/ \
        && rm -rf /tmp/kissat \
        || echo "WARN: kissat build failed (column self-skips)"

# --- d4 (#SAT exact model counter) ---
# d4v2 needs Boost (graph + multiprecision/gmp) and MPFR headers; installed in its own
# layer so the earlier competitor layers stay cached across a d4-only rebuild.
RUN apt-get update && apt-get install -y --no-install-recommends \
        libboost-all-dev libmpfr-dev \
    && rm -rf /var/lib/apt/lists/*
RUN git clone https://github.com/crillab/d4 /tmp/d4 \
        && cd /tmp/d4 && git checkout 333370cc1e843dd0749c1efe88516e72b5239174 \
        && make -j"$(nproc)" \
        && cp d4 /usr/local/bin/ \
        && rm -rf /tmp/d4 \
        || echo "WARN: d4 build failed (#SAT competitor column self-skips)"

# --- LogicNG BDD adapter (shaded jar baked in) ---
COPY logicng /opt/logicng-src
RUN cd /opt/logicng-src && mvn -q -DskipTests package \
        && cp target/logicng-adapter.jar /opt/logicng-adapter.jar \
        || echo "WARN: logicng build failed (column self-skips)"

COPY run_all_in_container.sh /usr/local/bin/run_all_in_container.sh
RUN chmod +x /usr/local/bin/run_all_in_container.sh

WORKDIR /work
ENTRYPOINT ["/usr/local/bin/run_all_in_container.sh"]
