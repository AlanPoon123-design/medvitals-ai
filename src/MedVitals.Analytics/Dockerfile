# Java JAR executable
FROM maven:3.9.6-eclipse-temurin-21 AS builder
WORKDIR /app
COPY pom.xml .
RUN mvn dependency:go-offline
COPY src ./src
RUN mvn clean package -DskipTests

# minimal runtime container
# glibc-based (Ubuntu) JRE: ONNX Runtime's native libraries are not built for Alpine/musl
FROM eclipse-temurin:21-jre
WORKDIR /app

# curl is used by the docker-compose healthcheck
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=builder /app/target/*.jar app.jar

# run as non-root user
RUN groupadd --system appgroup && useradd --system --gid appgroup --no-create-home appuser
USER appuser

EXPOSE 8080
# shell form via sh -c so $JAVA_OPTS is expanded; exec keeps java as PID 1 for proper signal handling
ENV JAVA_OPTS=""
ENTRYPOINT ["sh", "-c", "exec java $JAVA_OPTS -jar app.jar"]
