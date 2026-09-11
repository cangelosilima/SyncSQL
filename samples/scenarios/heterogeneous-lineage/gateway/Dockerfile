FROM oraclelinux:7-slim AS base

RUN yum -y install oracle-database-preinstall-19c unzip && yum clean all
ENV ORACLE_BASE=/opt/oracle \
    ORACLE_HOME=/opt/oracle/product/19c/gateway
RUN mkdir -p /opt/oracle /opt/install && chown -R oracle:oinstall /opt/oracle /opt/install

FROM base AS installer
COPY --chown=oracle:oinstall .cache/LINUX.X64_193000_gateways.zip /opt/install/gateways.zip
COPY --chown=oracle:oinstall install.sh /opt/install/install.sh
USER oracle
RUN bash /opt/install/install.sh
USER root
RUN /opt/oracle/oraInventory/orainstRoot.sh && "$ORACLE_HOME/root.sh"
USER oracle
RUN rm -rf /opt/install/gateways /opt/install/gateways.zip

FROM base
COPY --from=installer --chown=oracle:oinstall /opt/oracle /opt/oracle
COPY --from=installer /etc/oraInst.loc /etc/oraInst.loc
USER oracle
WORKDIR /opt/oracle
