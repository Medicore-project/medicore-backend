var appointmentConsumerOptions = new AppointmentConsumerOptions
        {
            BootstrapServers = kafkaBootstrapServers,
            Topic = configuration["Kafka:AppointmentConsumer:Topic"]
                ?? AppointmentConsumerOptions.DefaultTopic,
            GroupId = configuration["Kafka:AppointmentConsumer:GroupId"]
                ?? AppointmentConsumerOptions.DefaultGroupId,
            RetryDelay = TimeSpan.FromSeconds(retryDelaySeconds),
            MaxRetryAttempts = 5,
            EnableExponentialBackoff = true,
            BaseRetryDelay = TimeSpan.FromSeconds(2)
        };