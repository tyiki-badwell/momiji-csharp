#pragma once

#include "Class.g.h"

namespace winrt::winrtc_client::implementation
{
    struct Class : ClassT<Class>
    {
        Class() = default;

        int32_t MyProperty();
        void MyProperty(int32_t value);
    };
}

namespace winrt::winrtc_client::factory_implementation
{
    struct Class : ClassT<Class, implementation::Class>
    {
    };
}
