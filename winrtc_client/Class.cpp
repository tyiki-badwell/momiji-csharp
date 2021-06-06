#include "pch.h"
#include "Class.h"
#include "Class.g.cpp"

#include "pc\peer_connection.h"

namespace winrt::winrtc_client::implementation
{
    int32_t Class::MyProperty()
    {
        throw hresult_not_implemented();
    }

    void Class::MyProperty(int32_t /* value */)
    {
        throw hresult_not_implemented();
    }
}
