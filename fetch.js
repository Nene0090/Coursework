async function fetchWithAccess(login, password) {
    // Авторизация
    const authResponse = await fetch("https://localhost:5000", {
        method: "POST",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ login, password })
    });

    const authData = await authResponse.json();
    console.log(authData);

    if (authData.status !== "ok") {
        return { error: "auth_failed", details: authData };
    }

    const access = authData.access; // уровень доступа

    // Запрос данных от 1С с уровнем доступа
    const oneCResponse = await fetch("https://localhost:5000", {
        method: "POST",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ access })
    });

    const oneCData = await oneCResponse.json();
    console.log(oneCData);

    return {
        access: access,
        data: oneCData
    };
}