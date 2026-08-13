# Class Diagram: Security

```mermaid
classDiagram
    direction LR
    classDef external fill:#f9f9f9,stroke:#333,stroke-dasharray: 5 5
    class CryptoHelper {
        +GenerateKeyAndIV()
        +Encrypt(string, string, string)
        +Decrypt(string, string, string)
    }

```