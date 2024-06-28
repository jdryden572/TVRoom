
<div class="container">
    <div class="circle"></div>
</div>

<style>
    .container {
        position: relative;
        width: 40px;
        aspect-ratio: 1;
        --color: red;
    }

    /* pulsing effect borrowed from https://codepen.io/eereeska/pen/gOPdBZg */
    .circle {
        position: absolute;
        left: 50%;
        top: 50%;
        transform: translateX(-50%) translateY(-50%);
        width: 20px;
        height: 20px;
    }

    .circle::before {
        content: "";
        position: relative;
        display: block;
        width: 250%;
        height: 250%;
        box-sizing: border-box;
        margin-left: -75%;
        margin-top: -75%;
        border-radius: 45px;
        background-color: var(--color);
        animation: pulse 1.25s cubic-bezier(0.215, 0.61, 0.355, 1) infinite;
    }

    .circle::after {
        content: "";
        position: absolute;
        left: 0;
        top: 0;
        display: block;
        width: 100%;
        height: 100%;
        background-color: var(--color);
        border-radius: 50px;
        animation: circle 1.25s cubic-bezier(0.455, 0.03, 0.515, 0.955) -0.4s infinite;
    }

    @keyframes pulse {
        0% {
            transform: scale(0.33);
        }
        80%,
        100% {
            opacity: 0;
        }
    }

    @keyframes circle {
        0% {
            transform: scale(0.8);
        }
        50% {
            transform: scale(1);
        }
        100% {
            transform: scale(0.8);
        }
    }
</style>
