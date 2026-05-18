async function fetchWithAccess(login, password) {
    const authResponse = await fetch("http://192.168.1.50:5000", {
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

    const access = authData.access;

    const oneCResponse = await fetch("http://192.168.1.50:5000", {
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
